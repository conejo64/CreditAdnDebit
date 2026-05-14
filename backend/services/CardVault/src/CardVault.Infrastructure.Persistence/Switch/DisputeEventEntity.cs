using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Switch;

/// <summary>
/// Event history for dispute lifecycle.
/// </summary>
public sealed class DisputeEventEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid DisputeId { get; set; }

    [MaxLength(32)]
    public string Action { get; set; } = default!; // open/representment/close

    [MaxLength(256)]
    public string Notes { get; set; } = "";

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
