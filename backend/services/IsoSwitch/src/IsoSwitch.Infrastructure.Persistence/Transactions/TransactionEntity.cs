using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Transactions;

public sealed class TransactionEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TraceId { get; set; } = default!;

    public string? CorrelationId { get; set; }

    public string? IdempotencyKey { get; set; }

    public string RequestMti { get; set; } = "0100";

    public string Stan { get; set; } = "000000";

    public string TxType { get; set; } = "AUTH"; // AUTH | CAPTURE | REVERSAL_ADVICE

    public string? OriginalTraceId { get; set; }

    // Reversal workflow over the ORIGINAL transaction
    public string? ReversalState { get; set; } // REVERSAL_PENDING | REVERSAL_CONFIRMED | REVERSAL_FAILED
    public DateTimeOffset? ReversalConfirmedOn { get; set; }
    public string ConnectorId { get; set; } = default!;

    public string RequestJson { get; set; } = default!;
    public string? ResponseJson { get; set; }

    public string? ResponseCode { get; set; }

    // Cached ISO request fields (for correlations / reversals)
    public string? ProcessingCode { get; set; } // field 3
    public string? Amount12 { get; set; }       // field 4
    public string? Currency { get; set; }       // field 49
    public string? TerminalId { get; set; }     // field 41
    public string? MerchantId { get; set; }     // field 42

    public string? Decision { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedOn { get; set; }
    public DateTimeOffset? CompletedOn { get; set; }

    /// <summary>
    /// PENDING | COMPLETED | FAILED | REVERSED
    /// </summary>
    public string Status { get; set; } = "PENDING";

    /// <summary>
    /// When timeout occurs, transaction becomes IN_DOUBT and reversal may be scheduled.
    /// </summary>
    public bool InDoubt { get; set; } = false;

    public DateTimeOffset? ReversalScheduledOn { get; set; }
    public DateTimeOffset? ReversalAttemptedOn { get; set; }
    public string? ReversalStatus { get; set; } // PENDING | SENT | CONFIRMED | FAILED

    public int ReversalAttempts { get; set; } = 0;
}