using System.Net;
using System.Net.Http.Headers;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// Runtime auth-boundary integration tests for GET /api/collections/delinquencies.
/// Uses WebApplicationFactory to prove the endpoint enforces the CanViewCollections
/// policy at the HTTP layer — not only as a structural attribute check.
/// </summary>
[Collection("WebApp")]
public sealed class DelinquencyEndpointAuthTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CardVaultWebApplicationFactory _factory;

    public DelinquencyEndpointAuthTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── RED scenario 1: unauthenticated request must return 401 ─────────────
    [Fact]
    public async Task GetDelinquencies_WhenUnauthenticated_Returns401()
    {
        // Arrange — no Authorization header
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/collections/delinquencies");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GREEN scenario 2: authenticated Auditor has read access (v76 contract) ──
    [Fact]
    public async Task GetDelinquencies_WhenAuditorRole_Returns200()
    {
        // Arrange — Auditor role: included in CanViewCollections per v76 read-only spec.
        // Auditors can VIEW collections data; they cannot mutate (CanManageCollections excludes them).
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/collections/delinquencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — 200 OK; Auditor has read-only visibility
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GREEN scenario 3: Admin role returns 200 ────────────────────────────
    [Fact]
    public async Task GetDelinquencies_WhenAdminRole_Returns200()
    {
        // Arrange
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/collections/delinquencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — 200 OK; empty page is fine (InMemory DB has no seed delinquencies)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TRIANGULATE: Operator role also passes ───────────────────────────────
    [Fact]
    public async Task GetDelinquencies_WhenOperatorRole_Returns200()
    {
        // Arrange
        var token = _factory.GenerateJwt(roles: ["Operator"]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/collections/delinquencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TRIANGULATE: granular perm claim passes even without role ────────────
    [Fact]
    public async Task GetDelinquencies_WhenGranularPermClaim_Returns200()
    {
        // Arrange — no role, but has "perm":"collections:view" claim
        var token = _factory.GenerateJwt(roles: [], extraClaims: [("perm", "collections:view")]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/collections/delinquencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── READ-ONLY CONSTRAINT: POST on a GET-only route returns 405 ──────────
    /// <summary>
    /// Runtime proof that the collections delinquency surface is read-only.
    /// An authorized Admin attempting POST receives 405 Method Not Allowed,
    /// proving no mutation verb is routed on this endpoint.
    /// </summary>
    [Fact]
    public async Task PostDelinquencies_WhenAuthorized_Returns405()
    {
        // Arrange — fully authorized caller (Admin) attempting a mutation
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/collections/delinquencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.SendAsync(request);

        // Assert — 405 proves no POST handler is registered; read-only surface enforced at runtime
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
