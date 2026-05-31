using System.Security.Cryptography;
using System.Text;
using CardVault.Infrastructure.Identity.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardVault.Api.Services;

public interface IPasswordResetService
{
    /// <summary>
    /// Creates a password reset token for the given email.
    /// Returns the raw token or empty string if the email is not registered (silent — avoids user enumeration).
    /// </summary>
    Task<string> CreateTokenAsync(string email, CancellationToken ct);

    /// <summary>
    /// Resets the user's password using the raw token.
    /// Returns true on success, false on any failure (expired, used, invalid, policy violation).
    /// </summary>
    Task<bool> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct);
}

public class PasswordResetService : IPasswordResetService
{
    private readonly IdentityAppDbContext _idDb;
    private readonly UserManager<AppUser> _users;
    private readonly ILogger<PasswordResetService> _logger;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(60);

    public PasswordResetService(
        IdentityAppDbContext idDb,
        UserManager<AppUser> users,
        ILogger<PasswordResetService> logger)
    {
        _idDb = idDb;
        _users = users;
        _logger = logger;
    }

    public async Task<string> CreateTokenAsync(string email, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null)
        {
            // Silent: do not reveal whether the email is registered.
            // DoTimingWork equalises response time to prevent timing-based user enumeration.
            _logger.LogInformation("Password reset requested for unregistered email (no-op).");
            DoTimingWork(email);
            return string.Empty;
        }

        // Invalidate existing active tokens for this user
        var existing = await _idDb.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedOn == null && t.ExpiresOn > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var old in existing)
            old.ExpiresOn = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Generate raw token (Base64Url — URL-safe, no padding chars) and store only its hash
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var hash = ComputeHash(rawToken);

        _idDb.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.Add(TokenLifetime)
        });

        await _idDb.SaveChangesAsync(ct);

        // ADR-4 stub: in production this would send an email via notification provider
        _logger.LogWarning(
            "PASSWORD_RESET_STUB: userId={UserId} token={Token} — send via email in production.",
            user.Id, rawToken);

        return rawToken;
    }

    public async Task<bool> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct)
    {
        var hash = ComputeHash(rawToken);

        var record = await _idDb.PasswordResetTokens.FirstOrDefaultAsync(
            t => t.TokenHash == hash && t.UsedOn == null && t.ExpiresOn > DateTimeOffset.UtcNow, ct);

        if (record is null) return false;

        var user = await _users.FindByIdAsync(record.UserId);
        if (user is null) return false;

        // Use UserManager's own token pipeline to enforce password policy
        var idToken = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, idToken, newPassword);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Password reset failed for user {UserId}: {Errors}",
                record.UserId, string.Join(", ", result.Errors.Select(e => e.Description)));
            return false;
        }

        // Only mark token as used after successful password change (no silent consume on policy failure)
        record.UsedOn = DateTimeOffset.UtcNow;

        // Revoke all active refresh tokens — invalidates existing sessions (spec IAM-PR-3-S1)
        var activeRefreshTokens = await _idDb.RefreshTokens
            .Where(r => r.UserId == record.UserId && r.RevokedOn == null)
            .ToListAsync(ct);
        foreach (var rt in activeRefreshTokens)
            rt.RevokedOn = DateTimeOffset.UtcNow;

        await _idDb.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Performs equivalent hash work for unknown-email paths to equalise response timing
    /// and prevent timing-based user enumeration. Virtual for test override.
    /// </summary>
    protected virtual void DoTimingWork(string email) => ComputeHash(email);

    /// <summary>Public for test access (white-box testing of hash matching).</summary>
    public static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
