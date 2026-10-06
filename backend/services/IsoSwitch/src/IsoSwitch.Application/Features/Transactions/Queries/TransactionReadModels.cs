using IsoSwitch.Domain;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using IsoSwitch.Infrastructure.Persistence.Transactions;

namespace IsoSwitch.Application.Features.Transactions.Queries;

/// <summary>
/// Read model for a single transaction. The entity is never returned directly: its
/// <c>RequestJson</c>/<c>ResponseJson</c> blobs are re-masked with <see cref="CardDataMasking"/> on
/// every read, so rows written before masking was enforced cannot leak card data to a client.
/// </summary>
public sealed record TransactionDetailDto(
    string TraceId,
    string? CorrelationId,
    string? IdempotencyKey,
    string TxType,
    string RequestMti,
    string Stan,
    string Status,
    string? Decision,
    string? ResponseCode,
    string ConnectorId,
    string? ProcessingCode,
    string? Amount12,
    string? Currency,
    string? TerminalId,
    string? MerchantId,
    string? MaskedPan,
    string? OriginalTraceId,
    string? ReversalState,
    DateTimeOffset? ReversalConfirmedOn,
    string? ReversalStatus,
    DateTimeOffset? ReversalScheduledOn,
    DateTimeOffset? ReversalAttemptedOn,
    int ReversalAttempts,
    bool InDoubt,
    DateTimeOffset CreatedOn,
    DateTimeOffset? UpdatedOn,
    DateTimeOffset? CompletedOn,
    string? RequestJson,
    string? ResponseJson)
{
    public static TransactionDetailDto FromEntity(TransactionEntity tx) => new(
        tx.TraceId,
        tx.CorrelationId,
        tx.IdempotencyKey,
        tx.TxType,
        tx.RequestMti,
        tx.Stan,
        tx.Status,
        tx.Decision,
        tx.ResponseCode,
        tx.ConnectorId,
        tx.ProcessingCode,
        tx.Amount12,
        tx.Currency,
        tx.TerminalId,
        tx.MerchantId,
        CardDataMasking.ExtractMaskedPan(tx.RequestJson),
        tx.OriginalTraceId,
        tx.ReversalState,
        tx.ReversalConfirmedOn,
        tx.ReversalStatus,
        tx.ReversalScheduledOn,
        tx.ReversalAttemptedOn,
        tx.ReversalAttempts,
        tx.InDoubt,
        tx.CreatedOn,
        tx.UpdatedOn,
        tx.CompletedOn,
        CardDataMasking.MaskFieldsJson(tx.RequestJson),
        CardDataMasking.MaskFieldsJson(tx.ResponseJson));
}

/// <summary>Read model for one ISO audit log row, with <c>FieldsJson</c> re-masked on read.</summary>
public sealed record IsoMessageLogDto(
    string TraceId,
    string Direction,
    string Mti,
    string? FieldsJson,
    DateTimeOffset CreatedOn)
{
    public static IsoMessageLogDto FromEntity(IsoMessageLogEntity log) => new(
        log.TraceId,
        log.Direction,
        log.Mti,
        CardDataMasking.MaskFieldsJson(log.FieldsJson),
        log.CreatedOn);
}
