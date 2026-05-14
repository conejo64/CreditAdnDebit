using MediatR;

namespace IsoSwitch.Api.Features.Transactions.Commands.CaptureTransaction;

public record CaptureTransactionCommand(
    string TraceId,
    int Bin,
    decimal Amount,
    string Currency,
    string MerchantId,
    string TerminalId,
    string Stan,
    string? PinBlock = null,
    string? EmvTlv = null,
    string? Pan = null,
    string? ExpiryYyMm = null,
    string? PosEntryMode = null,
    string? PosConditionCode = null,
    string? Track2 = null,
    string? AdditionalAmounts54 = null,
    string? Private60 = null,
    string? Private61 = null,
    string? Private62 = null,
    string? IdempotencyKey = null
) : IRequest<CaptureTransactionResult>;

public record CaptureTransactionResult(
    string TraceId,
    string Status,
    string Decision,
    string ResponseCode,
    string ConnectorId,
    string? IdempotencyKey
);
