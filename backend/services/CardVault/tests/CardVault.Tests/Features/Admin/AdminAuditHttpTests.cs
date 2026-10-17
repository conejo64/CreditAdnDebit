using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CardVault.Application.Contracts;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Infrastructure.Persistence;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Tests.Features.Admin;

/// <summary>
/// G1-T6: every administrative mutation in <c>AdminController</c> leaves an audit event with
/// a stable event type, the acting administrator, the target, a before/after summary and the
/// outcome. Failures (validation, not found, Identity errors) are audited too. No payload
/// ever carries a password, a reset token or any other credential.
/// </summary>
public sealed class AdminAuditHttpTests : IDisposable
{
    private const string ActorUserId = "admin-actor-g1t6";
    private const string StrongPassword = "Str0ng-Admin-Passw0rd!";

    private readonly CardVaultWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminAuditHttpTests()
    {
        _factory = new CardVaultWebApplicationFactory();
        _client = _factory.CreateClient();
        var token = _factory.GenerateJwt(["Admin"], [("uid", ActorUserId)]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<string> SeedUserAsync(string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, StrongPassword)).Succeeded.Should().BeTrue();
        if (roles.Length > 0)
            (await users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<List<JsonElement>> EventsAsync(string eventType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var rows = await db.AuditEvents
            .Where(e => e.EventType == eventType)
            .OrderBy(e => e.OccurredOn)
            .ToListAsync();
        rows.Should().OnlyContain(e => e.Service == "CardVault");
        return rows.Select(e => JsonDocument.Parse(e.PayloadJson).RootElement.Clone()).ToList();
    }

    private async Task<List<string>> AllPayloadsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        return await db.AuditEvents.Select(e => e.PayloadJson).ToListAsync();
    }

    private static string Str(JsonElement payload, params string[] path)
    {
        var current = payload;
        foreach (var segment in path)
            current = current.GetProperty(segment);
        return current.ValueKind == JsonValueKind.String ? current.GetString()! : current.GetRawText();
    }

    private static string[] StrArray(JsonElement payload, params string[] path)
    {
        var current = payload;
        foreach (var segment in path)
            current = current.GetProperty(segment);
        return current.EnumerateArray().Select(x => x.GetString()!).ToArray();
    }

    private static void AssertSucceededBy(JsonElement payload)
    {
        Str(payload, "actorUserId").Should().Be(ActorUserId);
        Str(payload, "outcome").Should().Be("succeeded");
    }

    // ── users ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUser_WritesCreatedEvent_WithActorTargetAndRoles_AndNoPassword()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/users",
            new CreateAdminUserRequest("new.operator@example.com", StrongPassword, ["Operator"]));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var events = await EventsAsync("admin.user.created");
        events.Should().ContainSingle();
        var payload = events[0];
        AssertSucceededBy(payload);
        Str(payload, "target", "email").Should().Be("new.operator@example.com");
        Str(payload, "target", "userId").Should().NotBeNullOrWhiteSpace();
        StrArray(payload, "change", "roles").Should().BeEquivalentTo(["Operator"]);

        (await AllPayloadsAsync()).Should().NotContain(p => p.Contains(StrongPassword));
    }

    [Fact]
    public async Task CreateUser_RejectedByThePasswordPolicy_IsAuditedAsFailed_WithoutThePassword()
    {
        const string weakPassword = "weakpw";
        var response = await _client.PostAsJsonAsync("/api/admin/users",
            new CreateAdminUserRequest("weak.user@example.com", weakPassword, ["Operator"]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var events = await EventsAsync("admin.user.created");
        events.Should().ContainSingle();
        var payload = events[0];
        Str(payload, "actorUserId").Should().Be(ActorUserId);
        Str(payload, "outcome").Should().Be("failed");
        Str(payload, "reason").Should().Contain("PasswordTooShort");
        Str(payload, "target", "email").Should().Be("weak.user@example.com");

        (await AllPayloadsAsync()).Should().NotContain(p => p.Contains(weakPassword));
    }

    [Fact]
    public async Task DeleteUser_WritesDeletedEvent_WithTheRolesTheUserHad()
    {
        var userId = await SeedUserAsync("to.delete@example.com", "Auditor");

        var response = await _client.DeleteAsync($"/api/admin/users/{userId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var payload = (await EventsAsync("admin.user.deleted")).Should().ContainSingle().Subject;
        AssertSucceededBy(payload);
        Str(payload, "target", "userId").Should().Be(userId);
        Str(payload, "target", "email").Should().Be("to.delete@example.com");
        StrArray(payload, "change", "rolesBefore").Should().BeEquivalentTo(["Auditor"]);
    }

    [Fact]
    public async Task DeleteUser_ThatDoesNotExist_IsAuditedAsFailed()
    {
        var response = await _client.DeleteAsync("/api/admin/users/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var payload = (await EventsAsync("admin.user.deleted")).Should().ContainSingle().Subject;
        Str(payload, "actorUserId").Should().Be(ActorUserId);
        Str(payload, "outcome").Should().Be("failed");
        Str(payload, "reason").Should().Be("user_not_found");
        Str(payload, "target", "userId").Should().Be("does-not-exist");
    }

    [Fact]
    public async Task BlockAndUnblock_WriteOneEventEach_WithTheLockoutTransition()
    {
        var userId = await SeedUserAsync("to.block@example.com", "Operator");

        (await _client.PostAsync($"/api/admin/users/{userId}/block", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsync($"/api/admin/users/{userId}/unblock", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = (await EventsAsync("admin.user.blocked")).Should().ContainSingle().Subject;
        AssertSucceededBy(blocked);
        Str(blocked, "target", "userId").Should().Be(userId);
        blocked.GetProperty("change").GetProperty("lockoutEndBefore").ValueKind.Should().Be(JsonValueKind.Null);
        blocked.GetProperty("change").GetProperty("lockoutEndAfter").ValueKind.Should().Be(JsonValueKind.String);

        var unblocked = (await EventsAsync("admin.user.unblocked")).Should().ContainSingle().Subject;
        AssertSucceededBy(unblocked);
        Str(unblocked, "target", "userId").Should().Be(userId);
        unblocked.GetProperty("change").GetProperty("lockoutEndBefore").ValueKind.Should().Be(JsonValueKind.String);
        unblocked.GetProperty("change").GetProperty("lockoutEndAfter").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ResetPassword_WritesPasswordResetEvent_WithoutThePasswordOrTheResetToken()
    {
        var userId = await SeedUserAsync("to.reset@example.com", "Operator");
        const string newPassword = "N3w-Reset-Passw0rd!";

        var response = await _client.PostAsJsonAsync($"/api/admin/users/{userId}/reset-password",
            new ResetPasswordRequest(newPassword));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var payload = (await EventsAsync("admin.user.password_reset")).Should().ContainSingle().Subject;
        AssertSucceededBy(payload);
        Str(payload, "target", "userId").Should().Be(userId);

        var json = payload.GetRawText();
        json.Should().NotContain(newPassword);
        json.Should().NotContainEquivalentOf("token", "the reset token is a credential and never enters the audit trail");
        json.Should().NotContainEquivalentOf("password\":", "no password-valued field is ever written");
    }

    [Fact]
    public async Task ResetPassword_RejectedByThePasswordPolicy_IsAuditedAsFailed()
    {
        var userId = await SeedUserAsync("reset.weak@example.com", "Operator");

        var response = await _client.PostAsJsonAsync($"/api/admin/users/{userId}/reset-password",
            new ResetPasswordRequest("weakpw"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = (await EventsAsync("admin.user.password_reset")).Should().ContainSingle().Subject;
        Str(payload, "outcome").Should().Be("failed");
        Str(payload, "reason").Should().Contain("PasswordTooShort");
        payload.GetRawText().Should().NotContain("weakpw");
    }

    [Fact]
    public async Task UpdateUserRoles_WritesRolesChangedEvent_WithBeforeAndAfter()
    {
        var userId = await SeedUserAsync("roles.change@example.com", "Operator");

        var response = await _client.PutAsJsonAsync($"/api/admin/users/{userId}/roles",
            new UpdateUserRolesRequest(["Auditor", "Admin"]));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = (await EventsAsync("admin.user.roles_changed")).Should().ContainSingle().Subject;
        AssertSucceededBy(payload);
        Str(payload, "target", "userId").Should().Be(userId);
        StrArray(payload, "change", "before").Should().BeEquivalentTo(["Operator"]);
        StrArray(payload, "change", "after").Should().BeEquivalentTo(["Admin", "Auditor"]);
    }

    [Fact]
    public async Task UpdateUserRoles_WithAnUnknownRole_IsAuditedAsFailed()
    {
        var userId = await SeedUserAsync("roles.bad@example.com", "Operator");

        var response = await _client.PutAsJsonAsync($"/api/admin/users/{userId}/roles",
            new UpdateUserRolesRequest(["NoSuchRole"]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = (await EventsAsync("admin.user.roles_changed")).Should().ContainSingle().Subject;
        Str(payload, "outcome").Should().Be("failed");
        Str(payload, "reason").Should().Be("unknown_role");
        Str(payload, "target", "userId").Should().Be(userId);
    }

    [Fact]
    public async Task SetUserPermissions_WritesPermissionsChangedEvent_WithBeforeAndAfter()
    {
        var userId = await SeedUserAsync("perms.change@example.com", "Operator");
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(userId);
            await users.AddClaimAsync(user!, new Claim("perm", "ledger:view"));
        }

        var response = await _client.PutAsJsonAsync($"/api/admin/users/{userId}/permissions",
            new SetUserPermissionsRequest(["billing:view", "risk:manage"]));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = (await EventsAsync("admin.user.permissions_changed")).Should().ContainSingle().Subject;
        AssertSucceededBy(payload);
        StrArray(payload, "change", "before").Should().BeEquivalentTo(["ledger:view"]);
        StrArray(payload, "change", "after").Should().BeEquivalentTo(["billing:view", "risk:manage"]);
    }

    // ── roles ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoleLifecycle_WritesCreatedPermissionsChangedAndDeletedEvents()
    {
        (await _client.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest("Reviewer", "Custom role")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PutAsJsonAsync("/api/admin/roles/Reviewer/permissions", new SetRolePermissionsRequest(["ledger:view"])))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.DeleteAsync("/api/admin/roles/Reviewer"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var created = (await EventsAsync("admin.role.created")).Should().ContainSingle().Subject;
        AssertSucceededBy(created);
        Str(created, "target", "roleName").Should().Be("Reviewer");

        var changed = (await EventsAsync("admin.role.permissions_changed")).Should().ContainSingle().Subject;
        AssertSucceededBy(changed);
        StrArray(changed, "change", "before").Should().BeEmpty();
        StrArray(changed, "change", "after").Should().BeEquivalentTo(["ledger:view"]);

        var deleted = (await EventsAsync("admin.role.deleted")).Should().ContainSingle().Subject;
        AssertSucceededBy(deleted);
        Str(deleted, "target", "roleName").Should().Be("Reviewer");
    }

    [Fact]
    public async Task DeletingACanonicalRole_IsRefusedAndAuditedAsFailed()
    {
        var response = await _client.DeleteAsync("/api/admin/roles/Admin");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = (await EventsAsync("admin.role.deleted")).Should().ContainSingle().Subject;
        Str(payload, "outcome").Should().Be("failed");
        Str(payload, "reason").Should().Be("canonical_role");
        Str(payload, "target", "roleName").Should().Be("Admin");
    }

    [Fact]
    public async Task ReadOnlyEndpoints_WriteNoAuditEvent()
    {
        (await _client.GetAsync("/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/api/admin/roles")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        (await db.AuditEvents.CountAsync(e => e.EventType.StartsWith("admin."))).Should().Be(0);
    }
}
