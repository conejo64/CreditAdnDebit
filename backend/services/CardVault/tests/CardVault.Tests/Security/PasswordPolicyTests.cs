using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CardVault.Application.Contracts;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Security;

/// <summary>
/// G1-T2 (PCI DSS v4 8.3.6) and G1-T1 (8.3.4): pins the Identity password and lockout
/// policy, and proves every password-setting entry point rejects an 11-character password.
/// </summary>
public sealed class PasswordPolicyTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private const string ElevenCharPassword = "Short1234!a"; // 11 chars, otherwise compliant
    private readonly CardVaultWebApplicationFactory _factory;

    public PasswordPolicyTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private IdentityOptions ResolveIdentityOptions()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public void PasswordPolicy_RequiresAtLeastTwelveCharacters()
    {
        var options = ResolveIdentityOptions();

        options.Password.RequiredLength.Should().BeGreaterThanOrEqualTo(12, "PCI DSS v4 8.3.6");
        options.Password.RequireDigit.Should().BeTrue();
        options.Password.RequireUppercase.Should().BeTrue();
        options.Password.RequireLowercase.Should().BeTrue();
        options.Password.RequireNonAlphanumeric.Should().BeTrue();
    }

    [Fact]
    public void LockoutPolicy_FollowsPciDefaults()
    {
        var options = ResolveIdentityOptions();

        options.Lockout.MaxFailedAccessAttempts.Should().Be(10, "PCI DSS v4 8.3.4");
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(30), "PCI DSS v4 8.3.4");
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
    }

    [Fact]
    public async Task Register_ElevenCharacterPassword_IsRejectedWith400()
    {
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"register-{Guid.NewGuid():N}@demo.com", ElevenCharPassword));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PasswordTooShort");
    }

    [Fact]
    public async Task AdminCreateUser_ElevenCharacterPassword_IsRejectedWith400()
    {
        using var client = CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/admin/users",
            new CreateAdminUserRequest($"admin-create-{Guid.NewGuid():N}@demo.com", ElevenCharPassword, ["Operator"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PasswordTooShort");
    }

    [Fact]
    public async Task AdminResetPassword_ElevenCharacterPassword_IsRejectedWith400()
    {
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var email = $"admin-reset-{Guid.NewGuid():N}@demo.com";
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
            (await userManager.CreateAsync(user, "InitialPassword1234!")).Succeeded.Should().BeTrue();
            userId = user.Id;
        }

        using var client = CreateAdminClient();
        var response = await client.PostAsJsonAsync($"/api/admin/users/{userId}/reset-password",
            new ResetPasswordRequest(ElevenCharPassword));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PasswordTooShort");
    }
}
