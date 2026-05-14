using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.OpenBanking;

public sealed class OpenBankingClientEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ClientId { get; set; } = default!;

    [MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(256)]
    public string SecretHash { get; set; } = default!;

    [MaxLength(256)]
    public string AllowedScopes { get; set; } = default!;

    public bool Enabled { get; set; } = true;

    public bool AllowAllAccounts { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastTokenIssuedOn { get; set; }

    public List<OpenBankingClientAccountAccessEntity> AccountAccesses { get; set; } = new();
}

public sealed class OpenBankingClientAccountAccessEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ClientEntityId { get; set; }
    public OpenBankingClientEntity Client { get; set; } = default!;

    public Guid AccountId { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
