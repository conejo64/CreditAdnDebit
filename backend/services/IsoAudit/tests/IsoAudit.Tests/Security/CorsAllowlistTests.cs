using IsoAudit.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IsoAudit.Tests.Security;

/// <summary>
/// SEC-6: IsoAudit CORS policy must block non-allowlisted origins and echo back
/// only the exact origin that is present in the allowlist (ADR-4).
/// RED (Task 4.1): current AllowAnyOrigin policy fails both assertions.
/// GREEN (Task 4.2): WithOrigins(allowlist) policy satisfies both.
/// </summary>
public class CorsAllowlistTests
{
    private const string AllowedOrigin = "https://allowed.example.com";
    private const string EvilOrigin    = "https://evil.example.com";

    // Factory pre-configured with a single allowlisted origin injected via config.
    private static WebApplicationFactory<Program> BuildFactory()
    {
        var factory = new IsoAuditWebApplicationFactory();
        return factory.WithWebHostBuilder(b => b.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin));
    }

    [Fact(DisplayName = "Non-allowlisted origin: no Access-Control-Allow-Origin returned")]
    public async Task EvilOrigin_NoCorsHeader_Returned()
    {
        // Arrange
        using var factory = BuildFactory();
        using var client  = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", EvilOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert: evil origin must NOT receive a CORS header
        var hasAcaoHeader = response.Headers.Contains("Access-Control-Allow-Origin");
        Assert.False(
            hasAcaoHeader,
            $"Expected NO Access-Control-Allow-Origin header for evil origin '{EvilOrigin}', " +
            $"but the response contained: {(hasAcaoHeader ? string.Join(", ", response.Headers.GetValues("Access-Control-Allow-Origin")) : "(none)")}");
    }

    [Fact(DisplayName = "Allowlisted origin: Access-Control-Allow-Origin echoes the origin")]
    public async Task AllowlistedOrigin_CorsHeader_Returned()
    {
        // Arrange
        using var factory = BuildFactory();
        using var client  = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert: allowlisted origin must receive the echoed origin value
        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            $"Expected Access-Control-Allow-Origin header for allowlisted origin '{AllowedOrigin}'.");

        var headerValue = response.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();
        Assert.Equal(AllowedOrigin, headerValue);
    }
}
