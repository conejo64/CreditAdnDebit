using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Ecommerce3ds;

/// <summary>
/// Runtime auth-boundary integration tests for EcommerceThreeDsController.
/// Proves that every endpoint enforces the declared authorization policy at the
/// HTTP layer — not merely as a structural attribute — using WebApplicationFactory
/// with real JWT tokens.
///
/// Policies under test:
///   CanManageRisk  → Admin | Operator
///   CanViewAudit   → Admin | Auditor
/// </summary>
[Collection("WebApp")]
public sealed class EcommerceThreeDsEndpointAuthTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CardVaultWebApplicationFactory _factory;

    // Minimal valid-looking POST body for StartChallenge.
    // The card won't exist in the InMemory DB, but auth is evaluated before
    // business logic, so this is sufficient to distinguish 401/403 from business errors.
    private static readonly StringContent StartChallengeBody = new(
        """
        {
          "cardId": "00000000-0000-0000-0000-000000000001",
          "amount": 100.00,
          "currency": "USD",
          "merchantId": "TEST001",
          "merchantName": "Test Store",
          "merchantCountry": "US",
          "browserIpCountry": "US",
          "deviceChannel": "BROWSER"
        }
        """,
        Encoding.UTF8,
        "application/json");

    private static readonly StringContent VerifyChallengeBody = new(
        """{"otp": "123456"}""",
        Encoding.UTF8,
        "application/json");

    public EcommerceThreeDsEndpointAuthTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── POST /api/ecommerce/3ds/challenges ──────────────────────────────────

    [Fact]
    public async Task StartChallenge_WhenUnauthenticated_Returns401()
    {
        var response = await _client.PostAsync("/api/ecommerce/3ds/challenges", StartChallengeBody);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StartChallenge_WhenAuditorRole_Returns403()
    {
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        var request = AuthorizedPost("/api/ecommerce/3ds/challenges", StartChallengeBody, token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StartChallenge_WhenAdminRole_PassesAuth()
    {
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var request = AuthorizedPost("/api/ecommerce/3ds/challenges", StartChallengeBody, token);

        var response = await _client.SendAsync(request);

        // Auth passed — business logic runs (card not found → non-auth error)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StartChallenge_WhenOperatorRole_PassesAuth()
    {
        var token = _factory.GenerateJwt(roles: ["Operator"]);
        var request = AuthorizedPost("/api/ecommerce/3ds/challenges", StartChallengeBody, token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── POST /api/ecommerce/3ds/challenges/{id}/verify ──────────────────────

    [Fact]
    public async Task VerifyChallenge_WhenUnauthenticated_Returns401()
    {
        var id = Guid.NewGuid();
        var response = await _client.PostAsync($"/api/ecommerce/3ds/challenges/{id}/verify", VerifyChallengeBody);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VerifyChallenge_WhenAuditorRole_Returns403()
    {
        var id = Guid.NewGuid();
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        var request = AuthorizedPost($"/api/ecommerce/3ds/challenges/{id}/verify", VerifyChallengeBody, token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VerifyChallenge_WhenOperatorRole_PassesAuth()
    {
        var id = Guid.NewGuid();
        var token = _factory.GenerateJwt(roles: ["Operator"]);
        var request = AuthorizedPost($"/api/ecommerce/3ds/challenges/{id}/verify", VerifyChallengeBody, token);

        var response = await _client.SendAsync(request);

        // Auth passed — challenge not found → business error, not auth error
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── GET /api/ecommerce/3ds/challenges/{id} ──────────────────────────────

    [Fact]
    public async Task GetChallenge_WhenUnauthenticated_Returns401()
    {
        var id = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/ecommerce/3ds/challenges/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetChallenge_WhenOperatorRole_Returns403()
    {
        var id = Guid.NewGuid();
        var token = _factory.GenerateJwt(roles: ["Operator"]);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ecommerce/3ds/challenges/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetChallenge_WhenAuditorRole_Returns404ForUnknownId()
    {
        var id = Guid.NewGuid();
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ecommerce/3ds/challenges/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        // Auth passed; challenge does not exist in InMemory DB → 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/ecommerce/3ds/challenges ───────────────────────────────────

    [Fact]
    public async Task ListChallenges_WhenUnauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/ecommerce/3ds/challenges");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListChallenges_WhenOperatorRole_Returns403()
    {
        var token = _factory.GenerateJwt(roles: ["Operator"]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ecommerce/3ds/challenges");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListChallenges_WhenAdminRole_Returns200()
    {
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ecommerce/3ds/challenges");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        // Auth passed; empty InMemory DB → empty list, 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static HttpRequestMessage AuthorizedPost(string url, HttpContent body, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
