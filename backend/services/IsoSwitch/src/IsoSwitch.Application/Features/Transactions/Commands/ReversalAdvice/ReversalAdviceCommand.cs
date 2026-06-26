using MediatR;

namespace IsoSwitch.Application.Features.Transactions.Commands.ReversalAdvice;

public record ReversalAdviceCommand(
    string TraceId,
    string OriginalTraceId,
    string? IdempotencyKey = null
) : IRequest<ReversalAdviceResult>;

public record ReversalAdviceResult(
    string TraceId,
    string Status,
    string Decision,
    string ResponseCode,
    string ConnectorId,
    string? IdempotencyKey,
    string? OriginalTraceId,
    string Field90
);
