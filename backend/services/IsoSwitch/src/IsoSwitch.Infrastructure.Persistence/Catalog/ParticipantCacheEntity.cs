using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Catalog;

public sealed class ParticipantCacheEntity
{
    [Key]
    [MaxLength(32)]
    public string ParticipantId { get; set; } = default!; // issuer/acquirer id

    [MaxLength(16)]
    public string Type { get; set; } = "ISSUER"; // ISSUER / ACQUIRER

    public string Name { get; set; } = default!;
    public string? CountryCode { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}