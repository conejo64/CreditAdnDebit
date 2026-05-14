using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.IsoAudit;

public sealed class IsoMessageLogEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string TraceId { get; set; } = default!;

    [MaxLength(8)]
    public string Direction { get; set; } = default!; // OUT/IN

    [MaxLength(4)]
    public string Mti { get; set; } = default!;

    public string FieldsJson { get; set; } = default!;
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}