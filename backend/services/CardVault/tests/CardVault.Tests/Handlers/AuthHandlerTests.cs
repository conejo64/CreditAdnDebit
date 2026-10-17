using CardVault.Application.Features.Auth.Commands;
using CardVault.Application.Ports;
using CardVault.Application.Services;
using CardVault.Api.Security;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace CardVault.Tests.Handlers;

/// <summary>
/// Unit tests for auth command handlers.
///
/// Strategy:
/// - LoginCommandHandler early-exit branches (user not found, wrong password, MFA) are
///   fully testable because those code paths return before reaching TokenService.
/// - RefreshTokenCommandHandler rejection paths (invalid, expired, revoked tokens) are
///   testable because they also return before TokenService is called.
/// - Success paths that require TokenService are excluded here; TokenService is not
///   an interface and requires live ASP.NET Identity services — those paths belong in
///   integration / smoke tests.
/// </summary>
public sealed class AuthHandlerTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly IdentityAppDbContext _idDb;
    private readonly UserManager<AppUser> _userManager;

    public AuthHandlerTests()
    {
        _db = TestDbContextFactory.Create();

        var idOptions = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _idDb = new IdentityAppDbContext(idOptions);

        var store = Substitute.For<IUserStore<AppUser>>();
        // UserManager has 9 constructor params; pass null for everything except the store
        _userManager = Substitute.For<UserManager<AppUser>>(
            store, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// Creates a minimal IUserTokenService stub that only supports HashRefreshToken,
    /// which is needed even on rejection paths of RefreshTokenCommandHandler.
    /// </summary>
    private static IUserTokenService CreateHashOnlyTokenService()
    {
        var stub = Substitute.For<IUserTokenService>();
        stub.HashRefreshToken(Arg.Any<string>()).Returns(ci => TokenService.HashRefreshToken(ci.Arg<string>()));
        return stub;
    }

    /// <summary>
    /// G1-T3: token service stub for the login MFA branch — issues a fixed pending token.
    /// </summary>
    private static IUserTokenService CreateMfaPendingTokenService(string token = "pending-token", string jti = "jti-1")
    {
        var stub = Substitute.For<IUserTokenService>();
        stub.CreateMfaPendingToken(Arg.Any<AppUser>())
            .Returns(new MfaPendingTokenIssue(token, jti, DateTimeOffset.UtcNow.AddMinutes(5)));
        return stub;
    }

    public void Dispose()
    {
        _db.Dispose();
        _idDb.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // LoginCommandHandler — early-exit branches
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginCommand_UserNotFound_ShouldReturn401()
    {
        // TokenService is null because handler exits before using it
        var handler = new LoginCommandHandler(_userManager, null!, _idDb, null!);
        var request = new CardVault.Application.Contracts.LoginRequest("unknown@demo.com", "anypass");

        _userManager.FindByEmailAsync(request.Email).Returns((AppUser?)null);

        var result = await handler.Handle(new LoginCommand(request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task LoginCommand_WrongPassword_ShouldReturn401()
    {
        var handler = new LoginCommandHandler(_userManager, null!, _idDb, null!);
        var request = new CardVault.Application.Contracts.LoginRequest("user@demo.com", "wrongpass");

        var user = new AppUser { Id = "u1", UserName = "user@demo.com", Email = "user@demo.com" };
        _userManager.FindByEmailAsync(request.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(false);

        var result = await handler.Handle(new LoginCommand(request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task LoginCommand_MfaEnabled_ShouldReturn200WithMfaRequired_AndAPendingTokenInsteadOfASession()
    {
        var handler = new LoginCommandHandler(_userManager, CreateMfaPendingTokenService(), _idDb, null!);
        var request = new CardVault.Application.Contracts.LoginRequest("mfa@demo.com", "correctpass");

        var user = new AppUser { Id = "u2", UserName = "mfa@demo.com", Email = "mfa@demo.com" };
        _userManager.FindByEmailAsync(request.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(true);
        _userManager.GetTwoFactorEnabledAsync(user).Returns(true);

        var result = await handler.Handle(new LoginCommand(request), CancellationToken.None);

        // Must return 200 (not 401) with MfaRequired=true
        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(200);

        var ok = result as Ok<CardVault.Application.Contracts.AuthSessionResponse>;
        ok.Should().NotBeNull("handler must return Ok<AuthSessionResponse>");
        ok!.Value!.MfaRequired.Should().BeTrue();
        ok.Value.Message.Should().Be("MFA_REQUIRED");
        ok.Value.MfaPendingToken.Should().Be("pending-token");
        ok.Value.AccessToken.Should().BeNull();
        ok.Value.RefreshToken.Should().BeNull();
        _idDb.RefreshTokens.Should().BeEmpty("no session exists before the second factor");
        // G1-T3: the jti is recorded server-side so the token is single-use.
        await _userManager.Received(1).SetAuthenticationTokenAsync(
            user, MfaPendingChallenge.LoginProvider, MfaPendingChallenge.TokenName, "jti-1");
    }

    // ─────────────────────────────────────────────────────────────
    // G1-T1: lockout counting on every password check (PCI DSS v4 8.3.4)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginCommand_LockedOutUser_ShouldReturn401WithoutCheckingPassword()
    {
        var handler = new LoginCommandHandler(_userManager, null!, _idDb, null!);
        var request = new CardVault.Application.Contracts.LoginRequest("locked@demo.com", "correctpass");

        var user = new AppUser { Id = "u3", UserName = "locked@demo.com", Email = "locked@demo.com" };
        _userManager.FindByEmailAsync(request.Email).Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(true);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(true);

        var result = await handler.Handle(new LoginCommand(request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
        await _userManager.DidNotReceive().CheckPasswordAsync(user, Arg.Any<string>());
    }

    [Fact]
    public async Task LoginCommand_WrongPassword_ShouldCountAccessFailure()
    {
        var handler = new LoginCommandHandler(_userManager, null!, _idDb, null!);
        var request = new CardVault.Application.Contracts.LoginRequest("user@demo.com", "wrongpass");

        var user = new AppUser { Id = "u1", UserName = "user@demo.com", Email = "user@demo.com" };
        _userManager.FindByEmailAsync(request.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(false);

        await handler.Handle(new LoginCommand(request), CancellationToken.None);

        await _userManager.Received(1).AccessFailedAsync(user);
        await _userManager.DidNotReceive().ResetAccessFailedCountAsync(user);
    }

    [Fact]
    public async Task LoginCommand_CorrectPassword_ShouldResetAccessFailureCount()
    {
        // MFA-enabled path: the handler returns before issuing a session, which keeps the
        // test free of live Identity services while still passing the password check.
        var handler = new LoginCommandHandler(_userManager, CreateMfaPendingTokenService(), _idDb, null!);
        var request = new CardVault.Application.Contracts.LoginRequest("mfa@demo.com", "correctpass");

        var user = new AppUser { Id = "u2", UserName = "mfa@demo.com", Email = "mfa@demo.com" };
        _userManager.FindByEmailAsync(request.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(true);
        _userManager.GetTwoFactorEnabledAsync(user).Returns(true);

        await handler.Handle(new LoginCommand(request), CancellationToken.None);

        await _userManager.Received(1).ResetAccessFailedCountAsync(user);
        await _userManager.DidNotReceive().AccessFailedAsync(user);
    }

    [Fact]
    public async Task LoginCommand_UnknownUser_WrongPassword_LockedOut_ShouldShareOneResponseShape()
    {
        var handler = new LoginCommandHandler(_userManager, null!, _idDb, null!);

        _userManager.FindByEmailAsync("ghost@demo.com").Returns((AppUser?)null);

        var wrongUser = new AppUser { Id = "w", UserName = "wrong@demo.com", Email = "wrong@demo.com" };
        _userManager.FindByEmailAsync("wrong@demo.com").Returns(wrongUser);
        _userManager.CheckPasswordAsync(wrongUser, Arg.Any<string>()).Returns(false);

        var lockedUser = new AppUser { Id = "l", UserName = "locked@demo.com", Email = "locked@demo.com" };
        _userManager.FindByEmailAsync("locked@demo.com").Returns(lockedUser);
        _userManager.IsLockedOutAsync(lockedUser).Returns(true);

        var unknown = await handler.Handle(new LoginCommand(new CardVault.Application.Contracts.LoginRequest("ghost@demo.com", "x")), CancellationToken.None);
        var wrong = await handler.Handle(new LoginCommand(new CardVault.Application.Contracts.LoginRequest("wrong@demo.com", "x")), CancellationToken.None);
        var locked = await handler.Handle(new LoginCommand(new CardVault.Application.Contracts.LoginRequest("locked@demo.com", "x")), CancellationToken.None);

        var unknownJson = unknown.Should().BeAssignableTo<IValueHttpResult>().Which.Value;
        wrong.Should().BeAssignableTo<IValueHttpResult>().Which.Value.Should().BeEquivalentTo(unknownJson);
        locked.Should().BeAssignableTo<IValueHttpResult>().Which.Value.Should().BeEquivalentTo(unknownJson);
    }

    // ─────────────────────────────────────────────────────────────
    // G1-T3: MFA enrolment bound to the authenticated caller
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MfaEnableCommand_WrongPassword_ShouldCountAccessFailure()
    {
        var handler = new MfaEnableCommandHandler(_userManager);
        var request = new CardVault.Application.Contracts.MfaEnableRequest("wrongpass");

        var user = new AppUser { Id = "u1", UserName = "user@demo.com", Email = "user@demo.com" };
        _userManager.FindByIdAsync("u1").Returns(user);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(false);

        var result = await handler.Handle(new MfaEnableCommand("u1", request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await _userManager.Received(1).AccessFailedAsync(user);
        await _userManager.DidNotReceive().ResetAuthenticatorKeyAsync(user);
    }

    [Fact]
    public async Task MfaEnableCommand_LockedOutUser_ShouldReturn401WithoutCheckingPassword()
    {
        var handler = new MfaEnableCommandHandler(_userManager);
        var request = new CardVault.Application.Contracts.MfaEnableRequest("correctpass");

        var user = new AppUser { Id = "u3", UserName = "locked@demo.com", Email = "locked@demo.com" };
        _userManager.FindByIdAsync("u3").Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(true);

        var result = await handler.Handle(new MfaEnableCommand("u3", request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await _userManager.DidNotReceive().CheckPasswordAsync(user, Arg.Any<string>());
    }

    [Fact]
    public async Task MfaEnableCommand_UnknownCaller_ShouldReturn401()
    {
        var handler = new MfaEnableCommandHandler(_userManager);
        _userManager.FindByIdAsync("ghost").Returns((AppUser?)null);

        var result = await handler.Handle(
            new MfaEnableCommand("ghost", new CardVault.Application.Contracts.MfaEnableRequest("whatever")),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await _userManager.DidNotReceive().ResetAuthenticatorKeyAsync(Arg.Any<AppUser>());
    }

    // ─────────────────────────────────────────────────────────────
    // G1-T3: MFA verification gated by the pending token
    // ─────────────────────────────────────────────────────────────

    private static IUserTokenService CreateValidatingTokenService(MfaPendingTokenClaims? claims)
    {
        var stub = Substitute.For<IUserTokenService>();
        stub.ValidateMfaPendingToken(Arg.Any<string>()).Returns(claims);
        return stub;
    }

    [Fact]
    public async Task MfaVerifyCommand_InvalidPendingToken_ShouldReturn401WithoutLookingUpAnyUser()
    {
        var handler = new MfaVerifyCommandHandler(_userManager, CreateValidatingTokenService(null), _idDb, null!);
        var request = new CardVault.Application.Contracts.MfaVerifyRequest("garbage", "000000");

        var result = await handler.Handle(new MfaVerifyCommand(request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await _userManager.DidNotReceive().FindByIdAsync(Arg.Any<string>());
        await _userManager.DidNotReceive().VerifyTwoFactorTokenAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task MfaVerifyCommand_PendingTokenNotRecordedForTheUser_ShouldReturn401WithoutCheckingTheCode()
    {
        var user = new AppUser { Id = "u1", UserName = "user@demo.com", Email = "user@demo.com" };
        _userManager.FindByIdAsync("u1").Returns(user);
        _userManager.GetAuthenticationTokenAsync(user, MfaPendingChallenge.LoginProvider, MfaPendingChallenge.TokenName)
            .Returns("a-different-jti");
        var tokens = CreateValidatingTokenService(new MfaPendingTokenClaims("u1", "jti-1"));
        var handler = new MfaVerifyCommandHandler(_userManager, tokens, _idDb, null!);

        var result = await handler.Handle(
            new MfaVerifyCommand(new CardVault.Application.Contracts.MfaVerifyRequest("signed-but-consumed", "000000")),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await _userManager.DidNotReceive().VerifyTwoFactorTokenAsync(user, Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task MfaVerifyCommand_LockedOutUser_ShouldReturn401WithoutCheckingTheCode()
    {
        var user = new AppUser { Id = "u3", UserName = "locked@demo.com", Email = "locked@demo.com" };
        _userManager.FindByIdAsync("u3").Returns(user);
        _userManager.IsLockedOutAsync(user).Returns(true);
        _userManager.GetAuthenticationTokenAsync(user, MfaPendingChallenge.LoginProvider, MfaPendingChallenge.TokenName)
            .Returns("jti-1");
        var handler = new MfaVerifyCommandHandler(_userManager, CreateValidatingTokenService(new MfaPendingTokenClaims("u3", "jti-1")), _idDb, null!);

        var result = await handler.Handle(
            new MfaVerifyCommand(new CardVault.Application.Contracts.MfaVerifyRequest("pending", "000000")),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await _userManager.DidNotReceive().VerifyTwoFactorTokenAsync(user, Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task MfaVerifyCommand_WrongCode_ShouldReturn401_CountAccessFailure_AndKeepTheChallenge()
    {
        var user = new AppUser { Id = "u1", UserName = "user@demo.com", Email = "user@demo.com" };
        _userManager.FindByIdAsync("u1").Returns(user);
        _userManager.GetAuthenticationTokenAsync(user, MfaPendingChallenge.LoginProvider, MfaPendingChallenge.TokenName)
            .Returns("jti-1");
        _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, "000000").Returns(false);
        var handler = new MfaVerifyCommandHandler(_userManager, CreateValidatingTokenService(new MfaPendingTokenClaims("u1", "jti-1")), _idDb, null!);

        var result = await handler.Handle(
            new MfaVerifyCommand(new CardVault.Application.Contracts.MfaVerifyRequest("pending", "000000")),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        await _userManager.Received(1).AccessFailedAsync(user);
        await _userManager.DidNotReceive().RemoveAuthenticationTokenAsync(user, Arg.Any<string>(), Arg.Any<string>());
        _idDb.RefreshTokens.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────
    // RefreshTokenCommandHandler — rejection paths (no TokenService needed)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_TokenNotFound_ShouldReturn401()
    {
        var handler = NewRefreshHandler();
        var request = new CardVault.Application.Contracts.RefreshRequest("nonexistent-token-value");

        var result = await handler.Handle(new RefreshTokenCommand(request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task RefreshToken_ExpiredToken_ShouldReturn401()
    {
        var raw  = TokenService.GenerateRefreshToken();
        var hash = TokenService.HashRefreshToken(raw);

        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId    = "some-user",
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddSeconds(-1) // already expired
        });
        await _idDb.SaveChangesAsync();

        var handler = NewRefreshHandler();
        var result  = await handler.Handle(
            new RefreshTokenCommand(new CardVault.Application.Contracts.RefreshRequest(raw)),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
        // G1-T4: an expired-but-never-revoked token is not reuse.
        _db.AuditEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshToken_RevokedToken_ShouldReturn401()
    {
        var raw  = TokenService.GenerateRefreshToken();
        var hash = TokenService.HashRefreshToken(raw);

        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId    = "some-user",
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
            RevokedOn = DateTimeOffset.UtcNow.AddMinutes(-5) // revoked
        });
        await _idDb.SaveChangesAsync();

        var handler = NewRefreshHandler();
        var result  = await handler.Handle(
            new RefreshTokenCommand(new CardVault.Application.Contracts.RefreshRequest(raw)),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
    }

    // ─────────────────────────────────────────────────────────────
    // G1-T4: refresh-token reuse detection (family revocation + audit)
    // ─────────────────────────────────────────────────────────────

    private RefreshTokenCommandHandler NewRefreshHandler()
        => new(_idDb, _userManager, CreateHashOnlyTokenService(), null!, new AuditService(_db));

    private async Task<string> SeedTokenAsync(string userId, Guid familyId, bool revoked = false)
    {
        var raw = TokenService.GenerateRefreshToken();
        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = TokenService.HashRefreshToken(raw),
            FamilyId = familyId,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
            RevokedOn = revoked ? DateTimeOffset.UtcNow.AddMinutes(-5) : null
        });
        await _idDb.SaveChangesAsync();
        return raw;
    }

    [Fact]
    public async Task RefreshToken_PresentingARevokedToken_RevokesItsFamily_LeavesOtherFamiliesAlone_AndAudits()
    {
        var family = Guid.NewGuid();
        var otherFamily = Guid.NewGuid();
        var reused = await SeedTokenAsync("victim", family, revoked: true);
        var successor = await SeedTokenAsync("victim", family);
        var unrelated = await SeedTokenAsync("victim", otherFamily);

        var result = await NewRefreshHandler().Handle(
            new RefreshTokenCommand(new CardVault.Application.Contracts.RefreshRequest(reused)),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        var rows = await _idDb.RefreshTokens.ToListAsync();
        rows.Single(r => r.TokenHash == TokenService.HashRefreshToken(successor)).RevokedOn.Should().NotBeNull("the family dies");
        rows.Single(r => r.TokenHash == TokenService.HashRefreshToken(unrelated)).RevokedOn.Should().BeNull("another family is unaffected");

        var audit = _db.AuditEvents.Should().ContainSingle().Which;
        audit.EventType.Should().Be("auth.refresh.reuse_detected");
        audit.PayloadJson.Should().Contain("victim").And.Contain(family.ToString());
        audit.PayloadJson.Should().NotContain(TokenService.HashRefreshToken(reused)).And.NotContain(reused);
        // The user is never looked up on the reuse path: nothing about the account is needed.
        await _userManager.DidNotReceive().FindByIdAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task RefreshToken_PresentingTheRevokedTokenTwice_AuditsEachDetection_WithoutFailing()
    {
        var family = Guid.NewGuid();
        var reused = await SeedTokenAsync("victim", family, revoked: true);
        var handler = NewRefreshHandler();

        var first = await handler.Handle(new RefreshTokenCommand(new CardVault.Application.Contracts.RefreshRequest(reused)), CancellationToken.None);
        var second = await handler.Handle(new RefreshTokenCommand(new CardVault.Application.Contracts.RefreshRequest(reused)), CancellationToken.None);

        first.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        second.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(401);
        _db.AuditEvents.Should().HaveCount(2);
    }

    [Fact]
    public void RefreshTokenPolicy_DefaultsToSevenDays_AndHonoursConfiguration()
    {
        RefreshTokenPolicy.LifetimeDays(null).Should().Be(7);

        var config = Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();
        config["Jwt:RefreshTokenDays"].Returns("3");
        RefreshTokenPolicy.LifetimeDays(config).Should().Be(3);

        var invalid = Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();
        invalid["Jwt:RefreshTokenDays"].Returns("0");
        RefreshTokenPolicy.LifetimeDays(invalid).Should().Be(7, "a non-positive value falls back to the policy default");
    }

    // ─────────────────────────────────────────────────────────────
    // TokenService static helpers — no external dependencies
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_ShouldProduceNonEmptyBase64()
    {
        var token = TokenService.GenerateRefreshToken();
        token.Should().NotBeNullOrWhiteSpace();
        token.Length.Should().BeGreaterThan(80, "Base64 of 64 random bytes is 88 chars");
    }

    [Fact]
    public void HashRefreshToken_SameInput_ShouldProduceSameHash()
    {
        var token = TokenService.GenerateRefreshToken();
        TokenService.HashRefreshToken(token).Should().Be(
            TokenService.HashRefreshToken(token),
            "hash must be deterministic");
    }

    [Fact]
    public void HashRefreshToken_DifferentInputs_ShouldProduceDifferentHashes()
    {
        var h1 = TokenService.HashRefreshToken(TokenService.GenerateRefreshToken());
        var h2 = TokenService.HashRefreshToken(TokenService.GenerateRefreshToken());
        h1.Should().NotBe(h2, "different tokens must produce different hashes");
    }
}
