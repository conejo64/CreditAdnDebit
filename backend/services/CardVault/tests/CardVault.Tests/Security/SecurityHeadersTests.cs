using CardVault.Tests.Infrastructure;
using FluentAssertions;

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
}
