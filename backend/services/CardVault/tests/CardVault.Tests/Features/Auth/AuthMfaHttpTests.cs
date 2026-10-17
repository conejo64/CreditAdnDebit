using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardVault.Application.Contracts;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CardVault.Tests.Features.Auth;

/// <summary>
/// G1-T3: MFA enrolment is bound to the authenticated caller (plus a fresh password
/// re-check) and MFA verification during login is gated behind a short-lived, single-use
/// pending token issued by the login step. A password alone must neither reset the
/// authenticator key nor obtain a session, and the verify step must not reveal whether the
/// token or the code was wrong.
/// </summary>
public sealed class AuthMfaHttpTests : IDisposable
{
    private readonly CardVaultWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthMfaHttpTests()
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

    private async Task<AppUser> SeedUserAsync(string email, string password, bool mfaEnabled = false)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue(string.Join(",", result.Errors.Select(e => e.Description)));

        if (mfaEnabled)
        {
            (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.Should().BeTrue();
            (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.Should().BeTrue();
        }

        return user;
    }

    private async Task<(string? key, bool enabled)> ReadMfaStateAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByIdAsync(userId))!;
        return (await users.GetAuthenticatorKeyAsync(user), await users.GetTwoFactorEnabledAsync(user));
    }

    private HttpRequestMessage AuthenticatedEnableRequest(AppUser caller, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enable")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.GenerateJwt(["Operator"], [("uid", caller.Id)]));
        return request;
    }

    private async Task<string> LoginForPendingTokenAsync(string email, string password)
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("mfaPendingToken").GetString()!;
    }

    private Task<HttpResponseMessage> VerifyAsync(string? pendingToken, string code)
        => _client.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaPendingToken = pendingToken, code });

    /// <summary>
    /// Forges a pending-style token with the given signing credentials. Used to prove that
    /// expiry is enforced and that a token is worthless without the server-side single-use
    /// record (G1-T5: the credentials are ES256; the test host's private key or a stranger's).
    /// </summary>
    private static string ForgePendingToken(string userId, string jti, DateTime expires, SigningCredentials credentials)
    {
        var token = new JwtSecurityToken(
            issuer: CardVaultWebApplicationFactory.Issuer,
            audience: CardVaultWebApplicationFactory.Audience + ":mfa-pending",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim("uid", userId),
                new Claim(JwtRegisteredClaimNames.Jti, jti),
                new Claim("purpose", "mfa-pending")
            ],
            notBefore: expires.AddMinutes(-5),
            expires: expires,
            signingCredentials: credentials);
        token.Header["typ"] = "mfa-pending";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── mfa/enable ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MfaEnable_Anonymous_Returns401_AndLeavesTheAuthenticatorUntouched()
    {
        var user = await SeedUserAsync("mfa-anon@demo.com", "MfaEnrolPassword1!");

        var response = await _client.PostAsJsonAsync("/api/auth/mfa/enable",
            new { email = user.Email, password = "MfaEnrolPassword1!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a password alone over an anonymous call must not enrol MFA");
        var (key, enabled) = await ReadMfaStateAsync(user.Id);
        key.Should().BeNull();
        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task MfaEnable_AuthenticatedWithWrongPassword_Returns401_AndKeyIsUnchanged()
    {
        var user = await SeedUserAsync("mfa-wrongpw@demo.com", "MfaEnrolPassword2!", mfaEnabled: true);
        var (keyBefore, _) = await ReadMfaStateAsync(user.Id);

        var response = await _client.SendAsync(AuthenticatedEnableRequest(user, new { password = "NotThePassword1!" }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var (keyAfter, _) = await ReadMfaStateAsync(user.Id);
        keyAfter.Should().Be(keyBefore, "a failed re-authentication must not reset the authenticator key");
    }

    [Fact]
    public async Task MfaEnable_AuthenticatedWithCorrectPassword_ReturnsTheSharedKey()
    {
        var user = await SeedUserAsync("mfa-happy@demo.com", "MfaEnrolPassword3!");

        var response = await _client.SendAsync(AuthenticatedEnableRequest(user, new { password = "MfaEnrolPassword3!" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MfaEnableResponse>();
        var (key, enabled) = await ReadMfaStateAsync(user.Id);
        body!.ManualKey.Should().NotBeNullOrWhiteSpace().And.Be(key);
        body.OtpauthUri.Should().Contain(Uri.EscapeDataString(user.Email!));
        body.RecoveryCodes.Should().NotBeEmpty();
        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task MfaEnable_IgnoresAnyEmailInTheBody_AndOperatesOnTheCaller()
    {
        var caller = await SeedUserAsync("mfa-caller@demo.com", "MfaEnrolPassword4!");
        var victim = await SeedUserAsync("mfa-victim@demo.com", "MfaEnrolPassword5!");

        var response = await _client.SendAsync(AuthenticatedEnableRequest(caller,
            new { email = victim.Email, password = "MfaEnrolPassword4!" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadMfaStateAsync(victim.Id)).Should().Be(((string?)null, false), "the body e-mail must be ignored");
        (await ReadMfaStateAsync(caller.Id)).enabled.Should().BeTrue();
    }

    // ── login → pending token ────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithMfaEnabled_ReturnsPendingToken_AndNoSessionCookies()
    {
        await SeedUserAsync("mfa-login@demo.com", "MfaLoginPassword1!", mfaEnabled: true);

        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("mfa-login@demo.com", "MfaLoginPassword1!"));

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.Contains("Set-Cookie").Should().BeFalse("no session may exist before the second factor");
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("mfaRequired").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("mfaPendingToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("user").ValueKind.Should().Be(JsonValueKind.Null);

        var pending = new JwtSecurityTokenHandler().ReadJwtToken(body.RootElement.GetProperty("mfaPendingToken").GetString());
        pending.ValidTo.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(5).AddSeconds(5), "the pending token lives at most 5 minutes");
        pending.Audiences.Should().NotContain(CardVaultWebApplicationFactory.Audience,
            "a pending token must never be acceptable as a session token");
    }

    // ── mfa/verify ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MfaVerify_ValidTokenAndCode_IssuesCookies_AndTheTokenCannotBeReplayed()
    {
        var user = await SeedUserAsync("mfa-verify@demo.com", "MfaVerifyPassword1!", mfaEnabled: true);
        var pendingToken = await LoginForPendingTokenAsync(user.Email!, "MfaVerifyPassword1!");
        var (key, _) = await ReadMfaStateAsync(user.Id);
        var code = TotpTestHelper.CurrentCode(key!);

        var verify = await VerifyAsync(pendingToken, code);

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var setCookies = verify.Headers.GetValues("Set-Cookie").ToArray();
        setCookies.Should().Contain(c => c.StartsWith("cv_at=", StringComparison.OrdinalIgnoreCase));
        setCookies.Should().Contain(c => c.StartsWith("cv_rt=", StringComparison.OrdinalIgnoreCase));
        var verifyBody = await verify.Content.ReadAsStringAsync();
        verifyBody.Should().NotContain("mfaPendingToken\":\"", "token material never flows back after verification");

        var replay = await VerifyAsync(pendingToken, TotpTestHelper.CurrentCode(key!));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the pending token is single-use");
        replay.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task MfaVerify_WrongCode_Returns401_WithTheSameBodyAsABadToken_AndKeepsTheChallengeAlive()
    {
        var user = await SeedUserAsync("mfa-wrongcode@demo.com", "MfaVerifyPassword2!", mfaEnabled: true);
        var pendingToken = await LoginForPendingTokenAsync(user.Email!, "MfaVerifyPassword2!");
        var (key, _) = await ReadMfaStateAsync(user.Id);
        var rightCode = TotpTestHelper.CurrentCode(key!);
        var wrongCode = rightCode == "000000" ? "111111" : "000000";

        var wrongCodeResponse = await VerifyAsync(pendingToken, wrongCode);
        var badTokenResponse = await VerifyAsync("not-a-token", rightCode);

        wrongCodeResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        badTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrongCodeResponse.Content.ReadAsStringAsync()).Should().Be(
            await badTokenResponse.Content.ReadAsStringAsync(),
            "the caller must not learn whether the token or the code was wrong");

        var retry = await VerifyAsync(pendingToken, TotpTestHelper.CurrentCode(key!));
        retry.StatusCode.Should().Be(HttpStatusCode.OK, "a mistyped code does not burn the challenge");
    }

    [Fact]
    public async Task MfaVerify_ExpiredToken_Returns401()
    {
        var user = await SeedUserAsync("mfa-expired@demo.com", "MfaVerifyPassword3!", mfaEnabled: true);
        await LoginForPendingTokenAsync(user.Email!, "MfaVerifyPassword3!");
        var (key, _) = await ReadMfaStateAsync(user.Id);
        var expired = ForgePendingToken(user.Id, Guid.NewGuid().ToString("N"),
            DateTime.UtcNow.AddMinutes(-1), CardVaultWebApplicationFactory.CreateSigningCredentials());

        var response = await VerifyAsync(expired, TotpTestHelper.CurrentCode(key!));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MfaVerify_ForgedToken_WithoutTheServerSideRecord_Returns401()
    {
        var user = await SeedUserAsync("mfa-forged@demo.com", "MfaVerifyPassword4!", mfaEnabled: true);
        var (key, _) = await ReadMfaStateAsync(user.Id);
        var code = TotpTestHelper.CurrentCode(key!);

        // Correct key, correct shape, but no login happened: the single-use record is absent.
        var wellFormedButUnissued = ForgePendingToken(user.Id, Guid.NewGuid().ToString("N"),
            DateTime.UtcNow.AddMinutes(4), CardVaultWebApplicationFactory.CreateSigningCredentials());
        // Wrong key: a stranger's ES256 private key.
        var wrongKey = ForgePendingToken(user.Id, Guid.NewGuid().ToString("N"),
            DateTime.UtcNow.AddMinutes(4),
            CardVaultWebApplicationFactory.CreateSigningCredentials(
                BuildingBlocks.Security.EcdsaKeyMaterial.GenerateKeyPair().PrivateKeyPem));
        // A real session token for the same user must not pass as a pending token.
        var sessionToken = _factory.GenerateJwt(["Operator"], [("uid", user.Id)]);

        (await VerifyAsync(wellFormedButUnissued, code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await VerifyAsync(wrongKey, code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await VerifyAsync(sessionToken, code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MfaVerify_NoLongerAcceptsEmailAndPassword()
    {
        var user = await SeedUserAsync("mfa-legacy@demo.com", "MfaVerifyPassword5!", mfaEnabled: true);
        var (key, _) = await ReadMfaStateAsync(user.Id);

        var response = await _client.PostAsJsonAsync("/api/auth/mfa/verify",
            new { email = user.Email, password = "MfaVerifyPassword5!", code = TotpTestHelper.CurrentCode(key!) });

        // The legacy shape has no mfaPendingToken: [ApiController] model validation rejects
        // it as malformed (400) before any handler runs. Either rejection is acceptable; what
        // matters is that credentials alone never produce a session.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }
}

/// <summary>
/// Computes the current 6-digit TOTP exactly as ASP.NET Identity's
/// <c>AuthenticatorTokenProvider</c> does (Base32 key, HMAC-SHA1, 30-second step, RFC 6238
/// dynamic truncation), so tests can present a valid second factor without an authenticator app.
/// </summary>
internal static class TotpTestHelper
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string CurrentCode(string base32Key)
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestep = (ulong)(unixSeconds / 30);
        using var hmac = new HMACSHA1(FromBase32(base32Key));
        return ComputeTotp(hmac, timestep).ToString("D6");
    }

    private static int ComputeTotp(HashAlgorithm hashAlgorithm, ulong timestepNumber)
    {
        var timestepAsBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((long)timestepNumber));
        var hash = hashAlgorithm.ComputeHash(timestepAsBytes);
        var offset = hash[^1] & 0xf;
        var binaryCode = (hash[offset] & 0x7f) << 24
                         | (hash[offset + 1] & 0xff) << 16
                         | (hash[offset + 2] & 0xff) << 8
                         | (hash[offset + 3] & 0xff);
        return binaryCode % 1_000_000;
    }

    private static byte[] FromBase32(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(input.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var c in input)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException($"Invalid Base32 character '{c}'.");
            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }
        return output.ToArray();
    }
}
