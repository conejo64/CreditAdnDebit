using System.Security.Claims;
using CardVault.Api.Security;
using FluentAssertions;

namespace CardVault.Tests.Auth;

/// <summary>
/// TDD unit tests for the CanManageCollections authorization policy logic.
/// RED: written before CollectionsManage constant and CanManageCollections policy exist.
/// Spec: Admin and Operator are allowed; Auditor and anonymous are denied;
/// explicit collections:manage claim is allowed regardless of role.
/// </summary>
public sealed class CanManageCollectionsPolicyTests
{
    // Mirror the exact helper from Program.cs
    private static bool RoleOrPerm(ClaimsPrincipal user, string perm, params string[] roles)
        => roles.Any(user.IsInRole) || user.HasClaim("perm", perm);

    private static ClaimsPrincipal UserWithRole(string role)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, role),
        ], "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UserWithPerm(string perm)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim("perm", perm),
        ], "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void CanManageCollections_AdminAllowed()
    {
        var user = UserWithRole("Admin");
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsManage, "Admin", "Operator");
        result.Should().BeTrue();
    }

    [Fact]
    public void CanManageCollections_OperatorAllowed()
    {
        var user = UserWithRole("Operator");
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsManage, "Admin", "Operator");
        result.Should().BeTrue();
    }

    [Fact]
    public void CanManageCollections_AuditorDenied()
    {
        var user = UserWithRole("Auditor");
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsManage, "Admin", "Operator");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanManageCollections_AnonymousDenied()
    {
        var identity = new ClaimsIdentity(); // unauthenticated — no auth type
        var user = new ClaimsPrincipal(identity);
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsManage, "Admin", "Operator");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanManageCollections_GranularPermAllowed()
    {
        // A user with explicit collections:manage claim is allowed, regardless of role.
        var user = UserWithPerm(PermissionCatalog.CollectionsManage);
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsManage, "Admin", "Operator");
        result.Should().BeTrue();
    }
}
