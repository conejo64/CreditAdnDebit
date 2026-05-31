using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Issuer;

/// <summary>
/// GAP-7 (RED): HTTP integration tests for the card lifecycle endpoints
/// (unblock / cancel / replace) using WebApplicationFactory.
///
/// These tests prove auth boundaries and runtime response codes at the HTTP layer —
/// complementing the structural attribute tests in CardLifecycleEndpointTests.cs.
/// </summary>
[Collection("WebApp")]
public sealed class CardLifecycleHttpTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CardVaultWebApplicationFactory _factory;

    public CardLifecycleHttpTests(CardVaultWebApplicationFactory factory)
    {
        _factory  = factory;
        _client   = factory.CreateClient();
    }

    // ── Authorization boundary: unauthenticated → 401 ───────────────────────

    [Fact]
    public async Task UnblockCard_WhenUnauthenticated_Returns401()
    {
        var response = await _client.PostAsync(
            $"/api/issuer/cards/{Guid.NewGuid()}/unblock",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "IssuerController requires CanOperateIssuer policy");
    }

    [Fact]
    public async Task CancelCard_WhenUnauthenticated_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/issuer/cards/{Guid.NewGuid()}/cancel",
            new { reason = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReplaceCard_WhenUnauthenticated_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/issuer/cards/{Guid.NewGuid()}/replace",
            new { reason = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authorization boundary: wrong role → 403 ────────────────────────────

    [Fact]
    public async Task UnblockCard_WhenAuditorRole_Returns403()
    {
        var token = _factory.GenerateJwt(["Auditor"]);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/issuer/cards/{Guid.NewGuid()}/unblock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "Auditor role does not satisfy CanOperateIssuer");
    }

    // ── Authorised Admin: non-existent card → 404 ───────────────────────────

    [Fact]
    public async Task UnblockCard_NonExistentCard_WithAdminAuth_Returns404()
    {
        var token = _factory.GenerateJwt(["Admin"]);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/issuer/cards/{Guid.NewGuid()}/unblock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CancelCard_NonExistentCard_WithAdminAuth_Returns404()
    {
        var token = _factory.GenerateJwt(["Admin"]);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/issuer/cards/{Guid.NewGuid()}/cancel",
            new { reason = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task ReplaceCard_NonExistentCard_WithAdminAuth_Returns404()
    {
        var token = _factory.GenerateJwt(["Admin"]);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/issuer/cards/{Guid.NewGuid()}/replace");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { reason = "test" });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GAP-5: ReplaceCard response body must contain newCardId ─────────────

    /// <summary>
    /// GAP-5 HTTP-layer proof: the 201 response from replace must include
    /// a "newCardId" JSON key at the HTTP level (not just in the handler unit test).
    /// This test is triangulation for the handler unit test — both must pass.
    /// NOTE: This test requires a seeded card, so it uses a shared factory scope.
    /// Because InMemory is used and the factory is per-class, each test run is isolated.
    /// </summary>
    [Fact]
    public async Task ReplaceCard_ExistingCard_WithAdminAuth_Returns201WithNewCardId()
    {
        // Arrange — seed a card via the issue endpoint
        var adminToken = _factory.GenerateJwt(["Admin"]);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // We need a real card in the InMemory DB; seed via the issue API
        var customerResp = await _client.PostAsJsonAsync("/api/issuer/customers", new
        {
            fullName = "Test Customer",
            documentId = "TEST-DOC-001",
            email = "test@lifecycle.local",
            phone = "0000000000",
            documentType = "CEDULA",
            gender = "M",
            billingAddress = "Test St",
            statementAddress = "Test St",
            residenceCity = "Quito",
            statementCity = "Quito",
            cardDeliveryCity = "Quito"
        });
        customerResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var customerJson = await customerResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var customerId = customerJson.GetProperty("id").GetString()!;

        var accResp = await _client.PostAsJsonAsync("/api/issuer/accounts", new
        {
            customerId = customerId,
            accountType = 1,   // Credit
            productCode = "VISA",
            creditLimit = 5000
        });
        accResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var accJson = await accResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var accountId = accJson.GetProperty("id").GetString()!;

        var cardResp = await _client.PostAsJsonAsync("/api/issuer/cards/issue", new
        {
            accountId = accountId,
            bin = "411111",
            pan = "4111111111111111",
            expiryYyMm = "2812"
        });
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var cardJson = await cardResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var cardId = cardJson.GetProperty("id").GetString()!;

        // Activate the card
        var activateResp = await _client.PostAsync(
            $"/api/issuer/cards/{cardId}/activate", content: null);
        activateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — replace the active card
        var replaceResp = await _client.PostAsJsonAsync(
            $"/api/issuer/cards/{cardId}/replace",
            new { reason = "damaged" });

        // Assert — 201 Created with body { "newCardId": "..." }
        replaceResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await replaceResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        body.TryGetProperty("newCardId", out var newCardIdProp).Should().BeTrue(
            because: "spec HC-2-S3 requires response body to contain 'newCardId'");
        newCardIdProp.GetString().Should().NotBeNullOrWhiteSpace();

        _client.DefaultRequestHeaders.Authorization = null;
    }
}
