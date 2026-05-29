using CardVault.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD tests for the v77 mutation endpoints on DelinquencyController.
/// RED: written before the four new endpoints exist on the controller.
/// Verifies structure (route, policy), not full HTTP integration.
/// </summary>
public sealed class DelinquencyMutationEndpointTests
{
    private readonly Type _controllerType = typeof(DelinquencyController);

    // ─────────────────────────────────────────────
    // POST api/collections/delinquencies/{id}/contact-attempts
    // ─────────────────────────────────────────────

    [Fact]
    public void RegisterContactAttempt_MethodExists()
    {
        var method = _controllerType.GetMethod("RegisterContactAttempt");
        method.Should().NotBeNull("RegisterContactAttempt action must exist on DelinquencyController");
    }

    [Fact]
    public void RegisterContactAttempt_HasHttpPostAttribute()
    {
        var method = _controllerType.GetMethod("RegisterContactAttempt")!;
        method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void RegisterContactAttempt_HasCanManageCollectionsPolicy()
    {
        var method = _controllerType.GetMethod("RegisterContactAttempt")!;
        var authorizeAttrs = method.GetCustomAttributes<AuthorizeAttribute>();
        authorizeAttrs.Should().Contain(a => a.Policy == "CanManageCollections");
    }

    // ─────────────────────────────────────────────
    // POST api/collections/delinquencies/{id}/notes
    // ─────────────────────────────────────────────

    [Fact]
    public void AddNote_MethodExists()
    {
        var method = _controllerType.GetMethod("AddNote");
        method.Should().NotBeNull("AddNote action must exist on DelinquencyController");
    }

    [Fact]
    public void AddNote_HasHttpPostAttribute()
    {
        var method = _controllerType.GetMethod("AddNote")!;
        method.GetCustomAttributes<HttpPostAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void AddNote_HasCanManageCollectionsPolicy()
    {
        var method = _controllerType.GetMethod("AddNote")!;
        var authorizeAttrs = method.GetCustomAttributes<AuthorizeAttribute>();
        authorizeAttrs.Should().Contain(a => a.Policy == "CanManageCollections");
    }

    // ─────────────────────────────────────────────
    // GET api/collections/delinquencies/{id}/contact-attempts
    // ─────────────────────────────────────────────

    [Fact]
    public void GetContactAttempts_MethodExists()
    {
        var method = _controllerType.GetMethod("GetContactAttempts");
        method.Should().NotBeNull("GetContactAttempts action must exist on DelinquencyController");
    }

    [Fact]
    public void GetContactAttempts_HasHttpGetAttribute()
    {
        var method = _controllerType.GetMethod("GetContactAttempts")!;
        method.GetCustomAttributes<HttpGetAttribute>().Should().ContainSingle();
    }

    // ─────────────────────────────────────────────
    // GET api/collections/delinquencies/{id}/notes
    // ─────────────────────────────────────────────

    [Fact]
    public void GetNotes_MethodExists()
    {
        var method = _controllerType.GetMethod("GetNotes");
        method.Should().NotBeNull("GetNotes action must exist on DelinquencyController");
    }

    [Fact]
    public void GetNotes_HasHttpGetAttribute()
    {
        var method = _controllerType.GetMethod("GetNotes")!;
        method.GetCustomAttributes<HttpGetAttribute>().Should().ContainSingle();
    }
}
