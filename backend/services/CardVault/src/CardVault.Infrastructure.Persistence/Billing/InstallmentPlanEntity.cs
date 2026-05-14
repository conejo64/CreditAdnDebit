using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum InstallmentPlanStatus
{
    Active = 1,
    Completed = 2,
    Cancelled = 3,
    Delinquent = 4
}

public sealed class InstallmentPlanEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public decimal TotalAmount { get; set; }

    public int TotalInstallments { get; set; }

    public int RemainingInstallments { get; set; }

    public decimal InterestApr { get; set; }

    public InstallmentPlanStatus Status { get; set; } = InstallmentPlanStatus.Active;

    public string Description { get; set; } = default!;

    public Guid? OriginalLedgerEntryId { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<AmortizationScheduleEntity> AmortizationSchedule { get; set; } = new();
}
