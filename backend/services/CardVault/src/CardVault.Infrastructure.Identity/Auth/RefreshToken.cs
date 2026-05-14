namespace CardVault.Infrastructure.Identity.Auth;

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;

    // Store only a hash of the refresh token
    public string TokenHash { get; set; } = default!;
    public DateTimeOffset ExpiresOn { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedOn { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public string? Device { get; set; }
    public bool IsActive => RevokedOn is null && DateTimeOffset.UtcNow < ExpiresOn;
}