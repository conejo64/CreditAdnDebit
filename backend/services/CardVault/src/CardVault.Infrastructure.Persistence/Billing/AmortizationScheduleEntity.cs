using CardVault.Domain;
using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public sealed class AmortizationScheduleEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid PlanId { get; set; }

    public int InstallmentNumber { get; set; }

    public decimal PrincipalAmount { get; set; }

    public decimal InterestAmount { get; set; }

    public decimal TotalInstallmentAmount { get; set; }

    public DateTime DueDate { get; set; }

    public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

    public Guid? BilledStatementId { get; set; }

    public DateTimeOffset? BilledOn { get; set; }

    public DateTimeOffset? PaidOn { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
