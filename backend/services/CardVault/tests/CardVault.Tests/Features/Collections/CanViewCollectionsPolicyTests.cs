using System.Security.Claims;
using CardVault.Api.Security;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD unit tests for the CanViewCollections authorization policy logic.
/// Tests the RoleOrPerm predicate: Admin | Operator | Auditor access.
/// Auditor has READ access (collections:view) per v76 spec; write operations require CanManageCollections.
/// </summary>
public sealed class CanViewCollectionsPolicyTests
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
    public void Admin_ShouldPassCanViewCollections()
    {
        var user = UserWithRole("Admin");
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsView, "Admin", "Operator", "Auditor");
        result.Should().BeTrue();
    }

    [Fact]
    public void Operator_ShouldPassCanViewCollections()
    {
        var user = UserWithRole("Operator");
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsView, "Admin", "Operator", "Auditor");
        result.Should().BeTrue();
    }

    [Fact]
    public void Auditor_ShouldPassCanViewCollections()
    {
        // v76 spec: Auditor has read-only visibility of collections data.
        // CanViewCollections includes Auditor role; CanManageCollections excludes it.
        var user = UserWithRole("Auditor");
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsView, "Admin", "Operator", "Auditor");
        result.Should().BeTrue();
    }

    [Fact]
    public void UserWithCollectionsViewPerm_ShouldPassRegardlessOfRole()
    {
        // Explicit perm claim grants access even without role
        var user = UserWithPerm(PermissionCatalog.CollectionsView);
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsView, "Admin", "Operator", "Auditor");
        result.Should().BeTrue();
    }

    [Fact]
    public void UnauthenticatedUser_ShouldNotPassCanViewCollections()
    {
        var identity = new ClaimsIdentity(); // unauthenticated
        var user = new ClaimsPrincipal(identity);
        var result = RoleOrPerm(user, PermissionCatalog.CollectionsView, "Admin", "Operator", "Auditor");
        result.Should().BeFalse();
    }
}
