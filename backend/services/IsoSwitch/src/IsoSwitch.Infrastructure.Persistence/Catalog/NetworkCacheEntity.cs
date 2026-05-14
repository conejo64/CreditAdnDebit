using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Catalog;

public sealed class NetworkCacheEntity
{
    [Key]
    [MaxLength(16)]
    public string Code { get; set; } = default!; // VISA, MC, LOCAL, etc.

    public string Name { get; set; } = default!;

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}