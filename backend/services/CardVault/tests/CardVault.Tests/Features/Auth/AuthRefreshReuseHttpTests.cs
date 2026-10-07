using System.Net;
using System.Net.Http.Json;
using CardVault.Api.Security;
using CardVault.Application.Contracts;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Infrastructure.Persistence;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Tests.Features.Auth;

/// <summary>
/// G1-T4: refresh tokens belong to a family (one per login / MFA verification). Rotation
/// keeps the family; presenting a token that was already rotated or revoked by logout is
/// reuse: every live token in that family is revoked, an audit event is written (user and
/// family, never the token) and the caller gets the same generic 401. Other families keep
/// working.
/// </summary>
public sealed class AuthRefreshReuseHttpTests : IDisposable
{
    private const string ReuseEventType = "auth.refresh.reuse_detected";

    private readonly CardVaultWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthRefreshReuseHttpTests()
    {
        _factory = new CardVaultWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(string userId, string refreshToken)> LoginAsync(string email, string password)
    {
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
                (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
            }
            userId = user.Id;
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (userId, ExtractCookieValue(response, "cv_rt"));
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"cv_rt={refreshToken}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> LogoutAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"cv_rt={refreshToken}");
        return await _client.SendAsync(request);
    }

    /// <summary>Cookie value as the browser would echo it back (still URL-encoded).</summary>
    private static string ExtractCookieValue(HttpResponseMessage response, string name)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase));
        return header.Split(';')[0][(name.Length + 1)..];
    }

    /// <summary>The stored hash of a cookie value: the server hashes the decoded token.</summary>
    private static string HashOf(string cookieValue) => TokenService.HashRefreshToken(Uri.UnescapeDataString(cookieValue));

    private async Task<List<RefreshToken>> TokensOfAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var idDb = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
        return await idDb.RefreshTokens.Where(t => t.UserId == userId).OrderBy(t => t.CreatedOn).ToListAsync();
    }

    private async Task<List<string>> ReusePayloadsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        return await db.AuditEvents.Where(e => e.EventType == ReuseEventType).Select(e => e.PayloadJson).ToListAsync();
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LegitimateRotation_KeepsWorking_AndKeepsTheTokenInTheSameFamily()
    {
        var (userId, tokenA) = await LoginAsync("rotate-ok@demo.com", "RotateOkPassword1!");

        var rotated = await RefreshAsync(tokenA);

        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenB = ExtractCookieValue(rotated, "cv_rt");
        tokenB.Should().NotBe(tokenA);

        var tokens = await TokensOfAsync(userId);
        tokens.Should().HaveCount(2);
        tokens.Select(t => t.FamilyId).Distinct().Should().ContainSingle("rotation keeps the family")
            .Which.Should().NotBe(Guid.Empty);
        var rowA = tokens.Single(t => t.TokenHash == HashOf(tokenA));
        rowA.RevokedOn.Should().NotBeNull();
        rowA.ReplacedByTokenHash.Should().Be(HashOf(tokenB));
        (await ReusePayloadsAsync()).Should().BeEmpty("a legitimate rotation is not reuse");
    }

    [Fact]
    public async Task PresentingARotatedToken_RevokesTheWholeFamily_AndWritesAnAuditEvent()
    {
        var (userId, tokenA) = await LoginAsync("reuse-rotated@demo.com", "ReusePassword1!");
        var tokenB = ExtractCookieValue(await RefreshAsync(tokenA), "cv_rt");

        var reuse = await RefreshAsync(tokenA);

        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await reuse.Content.ReadAsStringAsync()).Should().Be("{\"message\":\"Unauthorized\"}",
            "reuse must not be distinguishable from any other refresh rejection");
        reuse.Headers.Contains("Set-Cookie").Should().BeFalse();

        var tokens = await TokensOfAsync(userId);
        tokens.Should().HaveCount(2).And.OnlyContain(t => t.RevokedOn != null, "the whole family is revoked");

        var afterReuse = await RefreshAsync(tokenB);
        afterReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the successor token dies with its family");

        var familyId = tokens.Select(t => t.FamilyId).Distinct().Single();
        var payloads = await ReusePayloadsAsync();
        // Both dead presentations are evidence: the handler cannot tell owner from thief.
        // The first detection is the one that killed the live successor.
        payloads.Should().HaveCount(2, "presenting A and then the dead B are two detections");
        payloads[0].Should().Contain(userId).And.Contain(familyId.ToString()).And.Contain("\"revokedTokens\":1");
        payloads[1].Should().Contain("\"revokedTokens\":0");
        foreach (var payload in payloads)
        {
            payload.Should().NotContain(Uri.UnescapeDataString(tokenA)).And.NotContain(Uri.UnescapeDataString(tokenB))
                .And.NotContain(HashOf(tokenA)).And.NotContain(HashOf(tokenB),
                "the audit trail never carries token material");
        }
    }

    [Fact]
    public async Task ReuseInOneFamily_DoesNotAffectAnotherFamilyOfTheSameUser()
    {
        var (userId, familyOneA) = await LoginAsync("two-families@demo.com", "TwoFamiliesPassword1!");
        var (_, familyTwoA) = await LoginAsync("two-families@demo.com", "TwoFamiliesPassword1!");
        await RefreshAsync(familyOneA);

        var reuse = await RefreshAsync(familyOneA);
        var otherFamily = await RefreshAsync(familyTwoA);

        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otherFamily.StatusCode.Should().Be(HttpStatusCode.OK, "a different login is a different family");

        var tokens = await TokensOfAsync(userId);
        tokens.Select(t => t.FamilyId).Distinct().Should().HaveCount(2, "each login opens its own family");
        tokens.Where(t => t.RevokedOn == null).Should().ContainSingle("only the rotated successor of family two is alive");
    }

    [Fact]
    public async Task PresentingATokenRevokedByLogout_IsTreatedAsReuse()
    {
        var (userId, tokenA) = await LoginAsync("reuse-logout@demo.com", "ReuseLogoutPassword1!");
        (await LogoutAsync(tokenA)).IsSuccessStatusCode.Should().BeTrue();

        var reuse = await RefreshAsync(tokenA);

        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReusePayloadsAsync()).Should().ContainSingle().Which.Should().Contain(userId);
    }

    [Fact]
    public async Task EachLogin_OpensANewNonEmptyFamily()
    {
        var (userId, _) = await LoginAsync("families@demo.com", "FamiliesPassword1!");
        await LoginAsync("families@demo.com", "FamiliesPassword1!");

        var tokens = await TokensOfAsync(userId);

        tokens.Should().HaveCount(2);
        tokens.Select(t => t.FamilyId).Should().OnlyHaveUniqueItems().And.NotContain(Guid.Empty);
    }
}
