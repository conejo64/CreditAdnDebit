using CardVault.Api.Features.Auth.Commands;
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
        var request = new CardVault.Api.Contracts.LoginRequest("unknown@demo.com", "anypass");

        _userManager.FindByEmailAsync(request.Email).Returns((AppUser?)null);

        var result = await handler.Handle(new LoginCommand(request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task LoginCommand_WrongPassword_ShouldReturn401()
    {
        var handler = new LoginCommandHandler(_userManager, null!, _idDb, null!);
        var request = new CardVault.Api.Contracts.LoginRequest("user@demo.com", "wrongpass");

        var user = new AppUser { Id = "u1", UserName = "user@demo.com", Email = "user@demo.com" };
        _userManager.FindByEmailAsync(request.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(false);

        var result = await handler.Handle(new LoginCommand(request), CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task LoginCommand_MfaEnabled_ShouldReturn200WithMfaRequired()
    {
        var handler = new LoginCommandHandler(_userManager, null!, _idDb, null!);
        var request = new CardVault.Api.Contracts.LoginRequest("mfa@demo.com", "correctpass");

        var user = new AppUser { Id = "u2", UserName = "mfa@demo.com", Email = "mfa@demo.com" };
        _userManager.FindByEmailAsync(request.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, request.Password).Returns(true);
        _userManager.GetTwoFactorEnabledAsync(user).Returns(true);

        var result = await handler.Handle(new LoginCommand(request), CancellationToken.None);

        // Must return 200 (not 401) with MfaRequired=true
        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(200);

        var ok = result as Ok<CardVault.Api.Contracts.AuthSessionResponse>;
        ok.Should().NotBeNull("handler must return Ok<AuthSessionResponse>");
        ok!.Value!.MfaRequired.Should().BeTrue();
        ok.Value.Message.Should().Be("MFA_REQUIRED");
    }

    // ─────────────────────────────────────────────────────────────
    // RefreshTokenCommandHandler — rejection paths (no TokenService needed)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_TokenNotFound_ShouldReturn401()
    {
        var handler = new RefreshTokenCommandHandler(_idDb, _userManager, null!, null!);
        var request = new CardVault.Api.Contracts.RefreshRequest("nonexistent-token-value");

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

        var handler = new RefreshTokenCommandHandler(_idDb, _userManager, null!, null!);
        var result  = await handler.Handle(
            new RefreshTokenCommand(new CardVault.Api.Contracts.RefreshRequest(raw)),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
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

        var handler = new RefreshTokenCommandHandler(_idDb, _userManager, null!, null!);
        var result  = await handler.Handle(
            new RefreshTokenCommand(new CardVault.Api.Contracts.RefreshRequest(raw)),
            CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
              .Which.StatusCode.Should().Be(401);
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
