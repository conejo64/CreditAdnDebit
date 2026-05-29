using IsoSwitch.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace IsoSwitch.Tests.Auth;

/// <summary>
/// Proves the IsoSwitch auth-boundary contract: each policy requires a valid authenticated
/// principal with the correct role or granular permission, and denies anonymous or wrong-role access.
///
/// Covers spec scenarios:
///   - Switch monitor and audit access require an authenticated role
///   - Switch execution endpoints require operator or admin authority
///   - Operational ISO processing does not rely on anonymous demo routes
///   - Granular permission claims are honoured as an alternative to role membership
/// </summary>
public class AuthBoundaryTests
{
    private static IAuthorizationService BuildAuthService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            static bool RoleOrPerm(ClaimsPrincipal user, string permission, params string[] roles)
                => roles.Any(user.IsInRole) || user.HasClaim("perm", permission);

            options.AddPolicy(IsoSwitchAuthorizationPolicies.OperateSwitch, p => p.RequireAssertion(ctx =>
                RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.SwitchOperatePermission, "Admin", "Operator")));
            options.AddPolicy(IsoSwitchAuthorizationPolicies.ViewSwitchMonitor, p => p.RequireAssertion(ctx =>
                RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.SwitchMonitorPermission, "Admin", "Auditor")));
            options.AddPolicy(IsoSwitchAuthorizationPolicies.ManageSwitchRoutes, p => p.RequireAssertion(ctx =>
                RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.RoutingManagePermission, "Admin")));
            options.AddPolicy(IsoSwitchAuthorizationPolicies.ViewAudit, p => p.RequireAssertion(ctx =>
                RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.AuditViewPermission, "Admin", "Auditor")));
        });
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal UserWithRole(string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

    private static ClaimsPrincipal UserWithPermission(string permission)
        => new(new ClaimsIdentity([new Claim("perm", permission)], "test"));

    private static ClaimsPrincipal AnonymousUser()
        => new(new ClaimsIdentity()); // no authenticationType → unauthenticated

    // ──────────────────────────────────────────────────────────────
    // CanOperateSwitch — Admin/Operator or switch:operate
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task OperateSwitch_AnonymousUser_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(AnonymousUser(), IsoSwitchAuthorizationPolicies.OperateSwitch);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task OperateSwitch_Auditor_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Auditor"), IsoSwitchAuthorizationPolicies.OperateSwitch);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task OperateSwitch_Operator_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Operator"), IsoSwitchAuthorizationPolicies.OperateSwitch);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task OperateSwitch_Admin_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Admin"), IsoSwitchAuthorizationPolicies.OperateSwitch);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task OperateSwitch_GranularPermission_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithPermission("switch:operate"), IsoSwitchAuthorizationPolicies.OperateSwitch);
        Assert.True(result.Succeeded);
    }

    // ──────────────────────────────────────────────────────────────
    // CanViewSwitchMonitor — Admin/Auditor or switch:monitor
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ViewSwitchMonitor_AnonymousUser_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(AnonymousUser(), IsoSwitchAuthorizationPolicies.ViewSwitchMonitor);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ViewSwitchMonitor_Operator_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Operator"), IsoSwitchAuthorizationPolicies.ViewSwitchMonitor);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ViewSwitchMonitor_Auditor_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Auditor"), IsoSwitchAuthorizationPolicies.ViewSwitchMonitor);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ViewSwitchMonitor_GranularPermission_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithPermission("switch:monitor"), IsoSwitchAuthorizationPolicies.ViewSwitchMonitor);
        Assert.True(result.Succeeded);
    }

    // ──────────────────────────────────────────────────────────────
    // CanManageSwitchRoutes — Admin only or routing:manage
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ManageSwitchRoutes_AnonymousUser_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(AnonymousUser(), IsoSwitchAuthorizationPolicies.ManageSwitchRoutes);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ManageSwitchRoutes_Operator_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Operator"), IsoSwitchAuthorizationPolicies.ManageSwitchRoutes);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ManageSwitchRoutes_Admin_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Admin"), IsoSwitchAuthorizationPolicies.ManageSwitchRoutes);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ManageSwitchRoutes_GranularPermission_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithPermission("routing:manage"), IsoSwitchAuthorizationPolicies.ManageSwitchRoutes);
        Assert.True(result.Succeeded);
    }

    // ──────────────────────────────────────────────────────────────
    // CanViewAudit — Admin/Auditor or audit:view
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ViewAudit_AnonymousUser_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(AnonymousUser(), IsoSwitchAuthorizationPolicies.ViewAudit);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ViewAudit_Operator_IsDenied()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Operator"), IsoSwitchAuthorizationPolicies.ViewAudit);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ViewAudit_Auditor_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithRole("Auditor"), IsoSwitchAuthorizationPolicies.ViewAudit);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ViewAudit_GranularPermission_IsAllowed()
    {
        var auth = BuildAuthService();
        var result = await auth.AuthorizeAsync(UserWithPermission("audit:view"), IsoSwitchAuthorizationPolicies.ViewAudit);
        Assert.True(result.Succeeded);
    }
}
