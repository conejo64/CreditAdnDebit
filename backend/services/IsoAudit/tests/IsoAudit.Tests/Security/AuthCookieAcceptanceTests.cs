using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using IsoAudit.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace IsoAudit.Tests.Security;

/// <summary>
/// SEC-03: IsoAudit must accept the access token from the same HttpOnly cookie CardVault
/// issues at login, when no Authorization header is present.
///
/// Regression guard. IsoAudit read a cookie named "cv_access" while CardVault issues
/// "cv_at" (see CardVault.Api/Security/AuthCookieWriter.cs), so every browser call to a
/// protected IsoAudit endpoint returned 401 even with a valid session — the same defect
/// that surfaced in IsoSwitch. The cookie name is duplicated as a literal in each service,
/// so a test — not the literal — is what keeps them aligned.
/// </summary>
public sealed class AuthCookieAcceptanceTests : IClassFixture<IsoAuditWebApplicationFactory>
{
    private const string ProtectedEndpoint = "/api/audit/logs?take=1";

    private readonly IsoAuditWebApplicationFactory _factory;

    public AuthCookieAcceptanceTests(IsoAuditWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Mints an access token shaped like the one CardVault issues: same issuer, audience
    /// and signing key the test host is configured with, carrying a role the audit.read
    /// policy accepts.
    /// </summary>
    private static string CreateAccessToken()
    {
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(IsoAuditWebApplicationFactory.TestJwtKey));

        var token = new JwtSecurityToken(
            issuer: "CardVault",
            audience: "CardSwitch",
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_AcceptsEquivalentBearerToken()
    {
        var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_RejectsRequestWithNoCredentials()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ProtectedEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
