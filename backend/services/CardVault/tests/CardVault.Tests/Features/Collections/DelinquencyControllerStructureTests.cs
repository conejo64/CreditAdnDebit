using CardVault.Api.Controllers;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// Structural tests for DelinquencyController: route, authorization attribute,
/// and controller class existence.
/// </summary>
public sealed class DelinquencyControllerStructureTests
{
    [Fact]
    public void DelinquencyController_ShouldExist()
    {
        var type = typeof(DelinquencyController);
        type.Should().NotBeNull();
    }

    [Fact]
    public void DelinquencyController_ShouldHaveApiControllerAttribute()
    {
        var type = typeof(DelinquencyController);
        type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ApiControllerAttribute), inherit: true)
            .Should().NotBeEmpty();
    }

    [Fact]
    public void DelinquencyController_RouteShouldBeApiCollections()
    {
        var type = typeof(DelinquencyController);
        var routeAttr = type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Mvc.RouteAttribute>()
            .FirstOrDefault();

        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("api/collections");
    }

    [Fact]
    public void DelinquencyController_ShouldHaveAuthorizeWithCanViewCollections()
    {
        var type = typeof(DelinquencyController);
        var authorizeAttrs = type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();

        authorizeAttrs.Should().Contain(a => a.Policy == "CanViewCollections");
    }
}
