using CardVault.Api.Security;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CardVault.Tests.Security;

/// <summary>
/// SEC-12 (task 4.12): every CardVault response must carry `X-Content-Type-Options:
/// nosniff`, `X-Frame-Options: DENY`, and a non-empty `Content-Security-Policy` whose
/// `frame-ancestors` directive is `'none'`. RED before the security-headers middleware
/// exists.
/// </summary>
[Collection("WebApp")]
public sealed class SecurityHeadersTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SecurityHeadersTests(CardVaultWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AnyResponse_IncludesNoSniffHeader()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.TryGetValues("X-Content-Type-Options", out var values).Should().BeTrue();
        values!.Should().Contain("nosniff");
    }

    [Fact]
    public async Task AnyResponse_DeniesFraming_ViaXFrameOptionsAndCsp()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.TryGetValues("X-Frame-Options", out var xfo).Should().BeTrue();
        xfo!.Should().Contain("DENY");

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).Should().BeTrue();
        csp!.First().Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task AnyResponse_HasNonEmptyContentSecurityPolicy()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).Should().BeTrue();
        csp!.First().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// SEC-03 hardening: outside Development the CSP must be strict — no 'unsafe-inline' —
    /// so the header keeps its XSS mitigation. Swagger (the only reason for the inline
    /// relaxation) is gated to Development, so production has no reason to relax it.
    /// </summary>
    [Fact]
    public async Task ProductionCsp_HasNoUnsafeInline_AndStillDeniesFraming()
    {
        var csp = await CaptureCspForEnvironment("Production");

        csp.Should().NotContain("unsafe-inline");
        csp.Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task DevelopmentCsp_AllowsUnsafeInline_ForSwaggerUi()
    {
        var csp = await CaptureCspForEnvironment("Development");

        csp.Should().Contain("'unsafe-inline'");
    }

    private static async Task<string> CaptureCspForEnvironment(string environmentName)
    {
        var middleware = new SecurityHeadersMiddleware(new TestHostEnvironment(environmentName));
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        return context.Response.Headers["Content-Security-Policy"].ToString();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "CardVault.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
