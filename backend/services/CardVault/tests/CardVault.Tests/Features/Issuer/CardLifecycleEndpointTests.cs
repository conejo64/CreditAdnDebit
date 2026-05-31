using CardVault.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace CardVault.Tests.Features.Issuer;

/// <summary>
/// TDD structural tests for the three new card lifecycle endpoints on IssuerController.
/// RED: written before UnblockCard, CancelCard, and ReplaceCard exist on the controller.
/// Verifies route attributes, HTTP verb, and authorization policy without full HTTP round-trip.
/// </summary>
public sealed class CardLifecycleEndpointTests
{
    private readonly Type _controllerType = typeof(IssuerController);

    // ─────────────────────────────────────────────
    // POST api/issuer/cards/{id}/unblock
    // ─────────────────────────────────────────────

    [Fact]
    public void UnblockCard_MethodExists()
    {
        var method = _controllerType.GetMethod("UnblockCard");
        method.Should().NotBeNull("UnblockCard action must exist on IssuerController");
    }

    [Fact]
    public void UnblockCard_HasHttpPostAttribute()
    {
        var method = _controllerType.GetMethod("UnblockCard")!;
        method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void UnblockCard_RouteContainsUnblock()
    {
        var method = _controllerType.GetMethod("UnblockCard")!;
        var route = method.GetCustomAttribute<HttpPostAttribute>()!.Template;
        route.Should().Contain("unblock", because: "the endpoint must be at .../unblock");
    }

    // ─────────────────────────────────────────────
    // POST api/issuer/cards/{id}/cancel
    // ─────────────────────────────────────────────

    [Fact]
    public void CancelCard_MethodExists()
    {
        var method = _controllerType.GetMethod("CancelCard");
        method.Should().NotBeNull("CancelCard action must exist on IssuerController");
    }

    [Fact]
    public void CancelCard_HasHttpPostAttribute()
    {
        var method = _controllerType.GetMethod("CancelCard")!;
        method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void CancelCard_RouteContainsCancel()
    {
        var method = _controllerType.GetMethod("CancelCard")!;
        var route = method.GetCustomAttribute<HttpPostAttribute>()!.Template;
        route.Should().Contain("cancel", because: "the endpoint must be at .../cancel");
    }

    // ─────────────────────────────────────────────
    // POST api/issuer/cards/{id}/replace
    // ─────────────────────────────────────────────

    [Fact]
    public void ReplaceCard_MethodExists()
    {
        var method = _controllerType.GetMethod("ReplaceCard");
        method.Should().NotBeNull("ReplaceCard action must exist on IssuerController");
    }

    [Fact]
    public void ReplaceCard_HasHttpPostAttribute()
    {
        var method = _controllerType.GetMethod("ReplaceCard")!;
        method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void ReplaceCard_RouteContainsReplace()
    {
        var method = _controllerType.GetMethod("ReplaceCard")!;
        var route = method.GetCustomAttribute<HttpPostAttribute>()!.Template;
        route.Should().Contain("replace", because: "the endpoint must be at .../replace");
    }

    // ─────────────────────────────────────────────
    // Controller-level auth policy
    // ─────────────────────────────────────────────

    [Fact]
    public void IssuerController_HasCanOperateIssuerPolicy()
    {
        var attr = _controllerType.GetCustomAttribute<AuthorizeAttribute>();
        attr.Should().NotBeNull();
        attr!.Policy.Should().Be("CanOperateIssuer");
    }
}
