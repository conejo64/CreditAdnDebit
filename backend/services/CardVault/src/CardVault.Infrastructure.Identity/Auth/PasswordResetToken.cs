namespace CardVault.Infrastructure.Identity.Auth;

public sealed class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;

    /// <summary>SHA-256 hex of the raw token sent to the user.</summary>
    public string TokenHash { get; set; } = default!;

    public DateTimeOffset ExpiresOn { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the token has been successfully consumed.</summary>
    public DateTimeOffset? UsedOn { get; set; }

    public bool IsActive => UsedOn is null && DateTimeOffset.UtcNow < ExpiresOn;
}
