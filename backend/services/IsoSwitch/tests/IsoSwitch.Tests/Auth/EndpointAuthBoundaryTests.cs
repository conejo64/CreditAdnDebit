using FluentAssertions;
using IsoSwitch.Tests.Auth.Support;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace IsoSwitch.Tests.Auth;

/// <summary>
/// Endpoint-level authorization proof for IsoSwitch operational endpoints.
///
/// Spec scenario: unauthenticated calls MUST return 401.
/// Spec scenario: authenticated calls with insufficient role/permission MUST return 403.
///
/// Test layer: Integration (WebApplicationFactory — tests HTTP middleware stack end-to-end).
/// TDD phase: RED → tests written first; IsoSwitchApiFactory added in GREEN step.
/// </summary>
public sealed class EndpointAuthBoundaryTests : IClassFixture<IsoSwitchApiFactory>
{
    private readonly IsoSwitchApiFactory _factory;

    public EndpointAuthBoundaryTests(IsoSwitchApiFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // 401 — Unauthenticated: no token at all
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/audit/latest?take=1")]
    [InlineData("GET", "/api/iso/audit/logs?take=1")]
    [InlineData("GET", "/api/routing/cache")]
    [InlineData("GET", "/api/catalog/cache/bins")]
    [InlineData("GET", "/api/catalog/cache/countries")]
    [InlineData("GET", "/api/catalog/cache/card-products")]
    [InlineData("GET", "/api/routing/rules/v2")]
    [InlineData("GET", "/api/catalog/currencies")]
    [InlineData("GET", "/api/catalog/networks")]
    [InlineData("GET", "/api/catalog/participants")]
    [InlineData("GET", "/api/catalog/bin-routes")]
    [InlineData("GET", "/api/transactions")]
    [InlineData("GET", "/api/iso/responses")]
    [InlineData("GET", "/api/responses")]
    [InlineData("GET", "/api/iso/traces")]
    public async Task Unauthenticated_GET_Returns401(string method, string path)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"{method} {path} must require authentication");
    }

    [Theory]
    [InlineData("POST", "/api/iso/authorize")]
    [InlineData("POST", "/api/iso/reversal")]
    [InlineData("POST", "/api/iso/capture")]
    [InlineData("POST", "/api/iso/reversal-advice")]
    [InlineData("POST", "/api/iso/network/ping")]
    [InlineData("POST", "/api/iso/network/signon")]
    [InlineData("POST", "/api/iso/network/signoff")]
    [InlineData("POST", "/api/simulate/purchase/approve")]
    [InlineData("POST", "/api/simulate/purchase/reverse")]
    [InlineData("POST", "/api/simulate/refund")]
    [InlineData("POST", "/api/simulate/chargeback")]
    [InlineData("POST", "/api/simulate/auth/approve")]
    [InlineData("POST", "/api/simulate/auth/reverse")]
    [InlineData("POST", "/api/simulate/clearing")]
    [InlineData("POST", "/api/iso/reconcile")]
    public async Task Unauthenticated_POST_Returns401(string method, string path)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"{method} {path} must require authentication");
    }

    // -------------------------------------------------------------------------
    // 403 — Authenticated but wrong role/permission (insufficient authorization)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/audit/latest?take=1")]
    [InlineData("GET", "/api/iso/audit/logs?take=1")]
    public async Task AuthenticatedAsOperator_AuditEndpoints_Return403(string method, string path)
    {
        // Operator has switch:operate but NOT audit:view
        var client = _factory.CreateClientWithRole("Operator");
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: $"Operator role must not access audit endpoints: {method} {path}");
    }

    [Theory]
    [InlineData("GET", "/api/routing/cache")]
    [InlineData("GET", "/api/catalog/cache/bins")]
    [InlineData("GET", "/api/catalog/cache/countries")]
    [InlineData("GET", "/api/catalog/cache/card-products")]
    [InlineData("GET", "/api/routing/rules/v2")]
    [InlineData("GET", "/api/catalog/currencies")]
    [InlineData("GET", "/api/catalog/networks")]
    [InlineData("GET", "/api/catalog/participants")]
    [InlineData("GET", "/api/catalog/bin-routes")]
    public async Task AuthenticatedAsAuditor_RoutingAndCatalogEndpoints_Return403(string method, string path)
    {
        // Auditor has audit:view but NOT routing:manage
        var client = _factory.CreateClientWithRole("Auditor");
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: $"Auditor role must not access routing/catalog admin endpoints: {method} {path}");
    }

    [Theory]
    [InlineData("POST", "/api/iso/authorize")]
    [InlineData("POST", "/api/iso/reversal")]
    [InlineData("POST", "/api/iso/capture")]
    [InlineData("POST", "/api/iso/network/ping")]
    [InlineData("POST", "/api/iso/network/signon")]
    [InlineData("POST", "/api/iso/network/signoff")]
    public async Task AuthenticatedAsAuditor_OperateEndpoints_Return403(string method, string path)
    {
        // Auditor has audit:view but NOT switch:operate
        var client = _factory.CreateClientWithRole("Auditor");
        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: $"Auditor role must not operate the switch: {method} {path}");
    }

    // -------------------------------------------------------------------------
    // Happy path: Anonymous endpoints remain accessible without a token
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/simulator/options")]
    [InlineData("GET", "/api/tcp/status")]
    [InlineData("GET", "/health")]
    public async Task AnonymousEndpoints_Return2xx_WithoutToken(string method, string path)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        ((int)response.StatusCode).Should().BeInRange(200, 299,
            because: $"{method} {path} must be publicly accessible");
    }
}
