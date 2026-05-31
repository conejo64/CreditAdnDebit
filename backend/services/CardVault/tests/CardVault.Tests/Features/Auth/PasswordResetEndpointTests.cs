using CardVault.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;

namespace CardVault.Tests.Features.Auth;

/// <summary>
/// TDD structural tests for ForgotPassword and ResetPassword endpoints on AuthController.
/// RED: written before the two new methods exist on the controller.
/// </summary>
public sealed class PasswordResetEndpointTests
{
    private readonly Type _controllerType = typeof(AuthController);

    // ─────────────────────────────────────────────
    // POST api/auth/forgot-password
    // ─────────────────────────────────────────────

    [Fact]
    public void ForgotPassword_MethodExists()
    {
        var method = _controllerType.GetMethod("ForgotPassword");
        method.Should().NotBeNull("ForgotPassword action must exist on AuthController");
    }

    [Fact]
    public void ForgotPassword_HasHttpPostAttribute()
    {
        var method = _controllerType.GetMethod("ForgotPassword")!;
        method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void ForgotPassword_HasAllowAnonymousAttribute()
    {
        var method = _controllerType.GetMethod("ForgotPassword")!;
        method.GetCustomAttributes<AllowAnonymousAttribute>().Should().ContainSingle(
            because: "password recovery must be accessible without authentication");
    }

    [Fact]
    public void ForgotPassword_RouteContainsForgotPassword()
    {
        var method = _controllerType.GetMethod("ForgotPassword")!;
        var route = method.GetCustomAttribute<HttpPostAttribute>()!.Template;
        route.Should().Contain("forgot-password");
    }

    // ─────────────────────────────────────────────
    // POST api/auth/reset-password
    // ─────────────────────────────────────────────

    [Fact]
    public void ResetPassword_MethodExists()
    {
        var method = _controllerType.GetMethod("ResetPassword");
        method.Should().NotBeNull("ResetPassword action must exist on AuthController");
    }

    [Fact]
    public void ResetPassword_HasHttpPostAttribute()
    {
        var method = _controllerType.GetMethod("ResetPassword")!;
        method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void ResetPassword_HasAllowAnonymousAttribute()
    {
        var method = _controllerType.GetMethod("ResetPassword")!;
        method.GetCustomAttributes<AllowAnonymousAttribute>().Should().ContainSingle(
            because: "password reset must be accessible without authentication");
    }

    [Fact]
    public void ResetPassword_RouteContainsResetPassword()
    {
        var method = _controllerType.GetMethod("ResetPassword")!;
        var route = method.GetCustomAttribute<HttpPostAttribute>()!.Template;
        route.Should().Contain("reset-password");
    }

    /// <summary>
    /// GAP-2 (RED): ResetPassword must be protected by the auth_password_reset rate-limit policy
    /// to prevent brute-force token guessing.  This test fails until
    /// [EnableRateLimiting("auth_password_reset")] is added to the action.
    /// </summary>
    [Fact]
    public void ResetPassword_HasEnableRateLimitingAttribute()
    {
        var method = _controllerType.GetMethod("ResetPassword")!;
        method.GetCustomAttributes<EnableRateLimitingAttribute>().Should().ContainSingle(
            because: "reset-password must be rate-limited via auth_password_reset policy to prevent brute-force attacks");
    }

    [Fact]
    public void ResetPassword_RateLimitingPolicy_IsAuthPasswordReset()
    {
        var method = _controllerType.GetMethod("ResetPassword")!;
        var attr = method.GetCustomAttribute<EnableRateLimitingAttribute>()!;
        attr.PolicyName.Should().Be("auth_password_reset",
            because: "the rate-limit policy name must match the registered policy in Program.cs");
    }
}
