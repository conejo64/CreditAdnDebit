using System.Net;
using System.Net.Http.Headers;
using CardVault.Tests.Infrastructure;

namespace CardVault.Tests.Security;

/// <summary>
/// SEC-7: POST /api/auth/register must be protected by the "CanManageUsersRoles" policy.
/// Anonymous callers → 401. Authenticated callers without the policy → 403.
/// Authenticated Admin (policy satisfied) → reaches the handler (not 401/403).
///
/// RED (Task 5.1): [AllowAnonymous] on Register means all three assertions fail
///                 until Task 5.2 swaps it for [Authorize(Policy="CanManageUsersRoles")].
/// GREEN (Task 5.2): attribute swap satisfies all three scenarios.
/// </summary>
public class RegisterLockdownTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;

    public RegisterLockdownTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── SEC-7 Scenario 1: Anonymous → 401 ────────────────────────────────────

    [Fact(DisplayName = "Anonymous POST /register returns 401")]
    public async Task AnonymousRegister_Returns401()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // No Authorization header — pure anonymous request with a minimal body
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        request.Content = new StringContent(
            """{"username":"ghost@test.com","password":"P@ssword1!","roles":[]}""",
            System.Text.Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.SendAsync(request);

        // Assert — anonymous caller must be rejected with 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── SEC-7 Scenario 2: Authenticated without policy → 403 ─────────────────

    [Fact(DisplayName = "Authenticated Auditor (no users:manage) POST /register returns 403")]
    public async Task AuthenticatedWithoutPolicy_Returns403()
    {
        // Arrange — Auditor role does NOT satisfy CanManageUsersRoles
        using var client = _factory.CreateClient();
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        request.Content = new StringContent(
            """{"username":"auditor-attempt@test.com","password":"P@ssword1!","roles":[]}""",
            System.Text.Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.SendAsync(request);

        // Assert — authenticated but insufficient privilege → 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── SEC-7 Scenario 3: Authenticated Admin → reaches handler (not 401/403) ─

    [Fact(DisplayName = "Authenticated Admin POST /register is not blocked (reaches handler)")]
    public async Task AuthenticatedWithPolicy_ReachesHandler()
    {
        // Arrange — Admin role satisfies CanManageUsersRoles
        using var client = _factory.CreateClient();
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // An intentionally incomplete/invalid body so the handler returns 400,
        // confirming it actually reached the handler rather than being blocked.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        request.Content = new StringContent(
            """{}""",
            System.Text.Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.SendAsync(request);

        // Assert — Admin reaches the handler; response is NOT 401 or 403
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden,    response.StatusCode);
    }
}
