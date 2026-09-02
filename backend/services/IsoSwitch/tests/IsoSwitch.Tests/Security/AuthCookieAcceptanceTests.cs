using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using FluentAssertions;
using IsoSwitch.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// SEC-03: IsoSwitch must accept the access token from the same HttpOnly cookie CardVault
/// issues at login, when no Authorization header is present.
///
/// Regression guard. IsoSwitch read a cookie named "cv_access" while CardVault issues
/// "cv_at" (see CardVault.Api/Security/AuthCookieWriter.cs), so every browser call to a
/// protected IsoSwitch endpoint returned 401 even with a valid session. The frontend
/// interceptor read that 401 as an expired session, refreshed, retried, failed again and
/// logged the user out. The cookie name is duplicated as a literal in each service, so a
/// test — not the literal — is what keeps them aligned.
/// </summary>
public sealed class AuthCookieAcceptanceTests : IClassFixture<IsoSwitchWebApplicationFactory>
{
    private const string ProtectedEndpoint = "/api/transactions?take=1";

    private readonly IsoSwitchWebApplicationFactory _factory;

    public AuthCookieAcceptanceTests(IsoSwitchWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Mints an access token shaped like the one CardVault issues: same issuer, audience
    /// and signing key the test host is configured with, carrying a role the
    /// ViewSwitchMonitor policy accepts.
    /// </summary>
    private static string CreateAccessToken()
    {
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(IsoSwitchWebApplicationFactory.TestJwtSigningKey));

        var token = new JwtSecurityToken(
            issuer: IsoSwitchWebApplicationFactory.Issuer,
            audience: IsoSwitchWebApplicationFactory.Audience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "cookie-acceptance-test-user"),
                new Claim(ClaimTypes.Role, "Admin")
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ProtectedEndpoint_AcceptsAccessTokenFromCardVaultCookie_WithoutAuthorizationHeader()
    {
        var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedEndpoint);
        request.Headers.Add("Cookie", $"cv_at={CreateAccessToken()}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().NotBe(
            HttpStatusCode.Unauthorized,
            "the cv_at cookie CardVault issues must authenticate an IsoSwitch request on its own");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_AcceptsEquivalentBearerToken()
    {
        var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the cookie path and the bearer path must authorize identically");
    }

    [Fact]
    public async Task ProtectedEndpoint_RejectsRequestWithNoCredentials()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ProtectedEndpoint);

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "an anonymous request must still be rejected — the cookie fallback must not weaken the endpoint");
    }
}
