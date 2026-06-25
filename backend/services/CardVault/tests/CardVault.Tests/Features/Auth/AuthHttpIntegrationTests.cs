using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CardVault.Application.Contracts;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Auth;

/// <summary>
/// GAP-1 + GAP-7 (RED): HTTP integration tests for auth/forgot-password and
/// auth/reset-password using WebApplicationFactory.
///
/// The forgot-password test proves the endpoint returns 202 Accepted
/// (not 204 No Content). It fails until AuthCommands.ForgotPasswordCommandHandler
/// is changed to return Results.Accepted() instead of Results.NoContent().
/// </summary>
[Collection("WebApp")]
public sealed class AuthHttpIntegrationTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthHttpIntegrationTests(CardVaultWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── GAP-1: POST /api/auth/forgot-password must return 202 ───────────────

    /// <summary>
    /// GAP-1 (RED): Spec HC-2-S4 requires forgot-password to return 202 Accepted.
    /// Current implementation returns 204 — this test fails until the handler
    /// is fixed to return Results.Accepted().
    /// </summary>
    [Fact]
    public async Task ForgotPassword_AnyEmail_ShouldReturn202Accepted()
    {
        // Act — no Authorization header required (AllowAnonymous)
        var response = await _client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest("anyone@test.com"));

        // Assert — 202 per spec HC-2-S4, NOT 204
        response.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            because: "spec HC-2-S4 requires forgot-password to return HTTP 202 (enumeration-safe)");
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_StillReturns202()
    {
        // Spec: even unknown emails must return 202 to prevent user enumeration
        var response = await _client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest("nobody@notexist.example"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ForgotPassword_RequiresNoAuthentication()
    {
        // Arrange — explicitly no Authorization header
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password");
        request.Content = JsonContent.Create(new ForgotPasswordRequest("noauth@test.com"));

        // Act
        var response = await _client.SendAsync(request);

        // Assert — 401 must never be returned for this endpoint
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            because: "forgot-password is [AllowAnonymous]");
    }

    // ── POST /api/auth/reset-password runtime contracts ─────────────────────

    [Fact]
    public async Task ResetPassword_InvalidToken_ShouldReturn400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordByTokenRequest("bad-token-that-does-not-exist", "NewPass123!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResetPassword_RequiresNoAuthentication()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password");
        request.Content = JsonContent.Create(
            new ResetPasswordByTokenRequest("any-token", "AnyPass1!"));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            because: "reset-password is [AllowAnonymous]");
    }
}
