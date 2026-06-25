using CardVault.Application.Services;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Cryptography;

namespace CardVault.Tests.Services;

/// <summary>
/// TDD unit tests for PasswordResetService.
/// RED: written before PasswordResetService, IPasswordResetService, and PasswordResetToken exist.
/// </summary>
public sealed class PasswordResetServiceTests : IDisposable
{
    private readonly IdentityAppDbContext _idDb;
    private readonly UserManager<AppUser> _userManager;

    public PasswordResetServiceTests()
    {
        var idOptions = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _idDb = new IdentityAppDbContext(idOptions);

        var store = Substitute.For<IUserStore<AppUser>>();
        _userManager = Substitute.For<UserManager<AppUser>>(
            store, null, null, null, null, null, null, null, null);
    }

    public void Dispose() => _idDb.Dispose();

    private PasswordResetService CreateService() =>
        new PasswordResetService(_idDb, _userManager, NullLogger<PasswordResetService>.Instance);

    // ─────────────────────────────────────────────────────────────
    // CreateTokenAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_UnknownEmail_ShouldReturnEmptyStringSilently()
    {
        // Arrange
        _userManager.FindByEmailAsync("unknown@demo.com").Returns((AppUser?)null);
        var svc = CreateService();

        // Act
        var token = await svc.CreateTokenAsync("unknown@demo.com", CancellationToken.None);

        // Assert — no exception; silent response protects against user enumeration
        token.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateToken_ExistingUser_ShouldReturnNonEmptyRawToken()
    {
        // Arrange
        var user = new AppUser { Id = "user-1", Email = "admin@demo.com", UserName = "admin@demo.com" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        var svc = CreateService();

        // Act
        var token = await svc.CreateTokenAsync(user.Email, CancellationToken.None);

        // Assert — a non-empty raw token is returned
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateToken_ExistingUser_ShouldPersistHashedTokenInDb()
    {
        // Arrange
        var user = new AppUser { Id = "user-2", Email = "ops@demo.com", UserName = "ops@demo.com" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        var svc = CreateService();

        // Act
        await svc.CreateTokenAsync(user.Email, CancellationToken.None);

        // Assert — one PasswordResetToken record saved for the user
        var saved = await _idDb.PasswordResetTokens
            .Where(t => t.UserId == user.Id)
            .ToListAsync();
        saved.Should().ContainSingle();
        saved[0].TokenHash.Should().NotBeNullOrWhiteSpace();
        saved[0].ExpiresOn.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateToken_CalledTwice_ShouldInvalidatePreviousToken()
    {
        // Arrange
        var user = new AppUser { Id = "user-3", Email = "twice@demo.com", UserName = "twice@demo.com" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        var svc = CreateService();

        // Act
        await svc.CreateTokenAsync(user.Email, CancellationToken.None);
        await svc.CreateTokenAsync(user.Email, CancellationToken.None);

        // Assert — only ONE active token (previous one expires immediately)
        var active = await _idDb.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedOn == null && t.ExpiresOn > DateTimeOffset.UtcNow)
            .ToListAsync();
        active.Should().ContainSingle("second call must expire the first token");
    }

    // ─────────────────────────────────────────────────────────────
    // ResetPasswordAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_InvalidToken_ShouldReturnFalse()
    {
        // Arrange
        var svc = CreateService();

        // Act — token that was never stored
        var result = await svc.ResetPasswordAsync("nonexistent-token", "NewPass123!", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_ShouldReturnFalse()
    {
        // Arrange
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var hash = PasswordResetService.ComputeHash(rawToken);
        _idDb.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = "user-expired",
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddSeconds(-1) // already expired
        });
        await _idDb.SaveChangesAsync();
        var svc = CreateService();

        // Act
        var result = await svc.ResetPasswordAsync(rawToken, "NewPass123!", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPassword_ValidToken_UserManagerSucceeds_ShouldReturnTrue()
    {
        // Arrange
        var userId = "user-valid";
        var user = new AppUser { Id = userId, Email = "valid@demo.com", UserName = "valid@demo.com" };
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var hash = PasswordResetService.ComputeHash(rawToken);

        _idDb.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(30)
        });
        await _idDb.SaveChangesAsync();

        _userManager.FindByIdAsync(userId).Returns(user);
        _userManager.GeneratePasswordResetTokenAsync(user).Returns("idtoken-123");
        _userManager.ResetPasswordAsync(user, "idtoken-123", "NewPass123!")
            .Returns(IdentityResult.Success);

        var svc = CreateService();

        // Act
        var result = await svc.ResetPasswordAsync(rawToken, "NewPass123!", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_ValidToken_ShouldMarkTokenAsUsed()
    {
        // Arrange
        var userId = "user-mark-used";
        var user = new AppUser { Id = userId, Email = "markused@demo.com", UserName = "markused@demo.com" };
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var hash = PasswordResetService.ComputeHash(rawToken);

        _idDb.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(30)
        });
        await _idDb.SaveChangesAsync();

        _userManager.FindByIdAsync(userId).Returns(user);
        _userManager.GeneratePasswordResetTokenAsync(user).Returns("idtoken-456");
        _userManager.ResetPasswordAsync(user, "idtoken-456", "NewPass123!")
            .Returns(IdentityResult.Success);

        var svc = CreateService();

        // Act
        await svc.ResetPasswordAsync(rawToken, "NewPass123!", CancellationToken.None);

        // Assert — token is marked as used
        var token = await _idDb.PasswordResetTokens.FirstAsync(t => t.TokenHash == hash);
        token.UsedOn.Should().NotBeNull("token must be marked used after successful reset");
    }

    [Fact]
    public async Task ResetPassword_UsedToken_ShouldReturnFalse()
    {
        // Arrange — token already consumed
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var hash = PasswordResetService.ComputeHash(rawToken);
        _idDb.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = "user-reused",
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(30),
            UsedOn = DateTimeOffset.UtcNow.AddMinutes(-5) // already used
        });
        await _idDb.SaveChangesAsync();
        var svc = CreateService();

        // Act
        var result = await svc.ResetPasswordAsync(rawToken, "NewPass123!", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────
    // GAP-4: Token lifetime must be exactly 60 minutes
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GAP-4 (RED): The spec (IAM-PR-2-S2) requires the reset token lifetime to be
    /// EXACTLY 60 minutes. The current implementation uses 30 minutes.
    /// This test fails until TokenLifetime is changed to 60 minutes.
    /// </summary>
    [Fact]
    public async Task CreateToken_ShouldExpireInExactly60Minutes()
    {
        // Arrange
        var user = new AppUser { Id = "user-lifetime", Email = "lifetime@demo.com", UserName = "lifetime@demo.com" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        var svc = CreateService();
        var before = DateTimeOffset.UtcNow;

        // Act
        await svc.CreateTokenAsync(user.Email, CancellationToken.None);

        // Assert — token must expire at now+60min (tolerance: ±5 seconds for test execution)
        var token = await _idDb.PasswordResetTokens.FirstAsync(t => t.UserId == user.Id);
        token.ExpiresOn.Should().BeCloseTo(
            before.AddMinutes(60),
            precision: TimeSpan.FromSeconds(5),
            because: "spec IAM-PR-2-S2 requires a 60-minute token lifetime");
    }

    // ─────────────────────────────────────────────────────────────
    // GAP-3: Refresh tokens must be revoked on successful password reset
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GAP-3 (RED): The spec (IAM-PR-3-S1) requires that ALL active refresh tokens
    /// for the user are invalidated when the password is successfully reset.
    /// This test fails until revocation logic is added to ResetPasswordAsync.
    /// </summary>
    [Fact]
    public async Task ResetPassword_Success_ShouldRevokeAllActiveRefreshTokens()
    {
        // Arrange
        var userId = "user-revoke-rt";
        var user = new AppUser { Id = userId, Email = "revokert@demo.com", UserName = "revokert@demo.com" };
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var hash = PasswordResetService.ComputeHash(rawToken);

        _idDb.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(60)
        });

        // Two active refresh tokens for this user
        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = "rt-hash-1",
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
        });
        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = "rt-hash-2",
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7)
        });
        await _idDb.SaveChangesAsync();

        _userManager.FindByIdAsync(userId).Returns(user);
        _userManager.GeneratePasswordResetTokenAsync(user).Returns("idtoken-revoke");
        _userManager.ResetPasswordAsync(user, "idtoken-revoke", "NewPass123!")
            .Returns(IdentityResult.Success);

        var svc = CreateService();

        // Act
        var result = await svc.ResetPasswordAsync(rawToken, "NewPass123!", CancellationToken.None);

        // Assert — password reset succeeded AND all refresh tokens are now revoked
        result.Should().BeTrue();
        var refreshTokens = await _idDb.RefreshTokens
            .Where(r => r.UserId == userId)
            .ToListAsync();
        refreshTokens.Should().AllSatisfy(r =>
            r.RevokedOn.Should().NotBeNull(
                because: "all active refresh tokens must be revoked when the password is reset (spec IAM-PR-3-S1)"));
    }

    /// <summary>
    /// GAP-3 (triangulate): A previously-revoked refresh token must remain revoked
    /// (its RevokedOn should not be overwritten with a later timestamp).
    /// </summary>
    // ─────────────────────────────────────────────────────────────
    // GAP-1: Token must use Base64Url encoding (URL-safe, no padding)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GAP-1 (RED): Spec IAM-PR-2-S1 requires the raw token to be URL-safe (Base64Url).
    /// The previous implementation used Convert.ToBase64String which always produces
    /// a trailing '=' for 32-byte input, breaking URL query parameters.
    /// Fixed by using WebEncoders.Base64UrlEncode.
    /// </summary>
    [Fact]
    public async Task CreateToken_ShouldBeBase64UrlEncoded_NoUnsafeCharsOrPadding()
    {
        // Arrange
        var user = new AppUser { Id = "user-b64url", Email = "b64url@demo.com", UserName = "b64url@demo.com" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        var svc = CreateService();

        // Act
        var token = await svc.CreateTokenAsync(user.Email, CancellationToken.None);

        // Assert — Base64 of 32 bytes always ends with '='; Base64Url never pads
        token.Should().NotContain("=",
            because: "Base64Url omits '=' padding; standard Base64 of 32 bytes always produces one trailing '='");
        token.Should().NotContain("+", because: "Base64Url uses '-' not '+'");
        token.Should().NotContain("/", because: "Base64Url uses '_' not '/'");
        token.Should().HaveLength(43,
            because: "Base64Url of 32 bytes is exactly 43 chars without padding (standard Base64 is 44)");
    }

    // ─────────────────────────────────────────────────────────────
    // GAP-2: Timing protection for unknown-email path
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GAP-2 (RED): The unknown-email branch must call DoTimingWork to equalise response
    /// time and prevent user-enumeration via timing attacks (spec IAM-PR-2-S3).
    /// Uses a test-double subclass — compiles only after PasswordResetService is unsealed
    /// and declares the virtual DoTimingWork method.
    /// </summary>
    [Fact]
    public async Task CreateToken_UnknownEmail_ShouldCallDoTimingWork()
    {
        _userManager.FindByEmailAsync("unknown@demo.com").Returns((AppUser?)null);

        var tracked = new TrackingPasswordResetService(
            _idDb, _userManager, NullLogger<PasswordResetService>.Instance);

        await tracked.CreateTokenAsync("unknown@demo.com", CancellationToken.None);

        tracked.TimingWorkCallCount.Should().Be(1,
            because: "unknown-email path must call DoTimingWork once to equalise timing with known-email path");
    }

    /// <summary>Test-double — only compiles once PasswordResetService is unsealed.</summary>
    private sealed class TrackingPasswordResetService : PasswordResetService
    {
        public int TimingWorkCallCount { get; private set; }

        public TrackingPasswordResetService(
            IdentityAppDbContext db,
            UserManager<AppUser> users,
            ILogger<PasswordResetService> logger)
            : base(db, users, logger) { }

        protected override void DoTimingWork(string email)
        {
            TimingWorkCallCount++;
            base.DoTimingWork(email);
        }
    }
}
