using IsoSwitch.Application.Config;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IsoSwitch.Application.Features.Transactions.Commands.ReversalAdvice;

public class ReversalAdviceCommandHandler : IRequestHandler<ReversalAdviceCommand, ReversalAdviceResult>
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;
    private readonly Field90Service _field90Svc;
    private readonly IMacService _macSvc;

    public ReversalAdviceCommandHandler(
        IsoSwitchDbContext db,
        ConnectorRegistry registry,
        ISwitchEventPublisher publisher,
        IIsoAuditService audit,
        Field90Service field90Svc,
        IMacService macSvc)
    {
        _db = db;
        _registry = registry;
        _publisher = publisher;
        _audit = audit;
        _field90Svc = field90Svc;
        _macSvc = macSvc;
    }

    public async Task<ReversalAdviceResult> Handle(ReversalAdviceCommand request, CancellationToken ct)
    {
        // 1. Idempotency Check
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == request.IdempotencyKey && t.TxType == TransactionTypes.ReversalAdvice, ct);

            if (existing is not null)
            {
                return new ReversalAdviceResult(
                    existing.TraceId,
                    existing.Status,
                    existing.Decision,
                    existing.ResponseCode ?? "XX",
                    existing.ConnectorId,
                    existing.IdempotencyKey,
                    existing.OriginalTraceId,
                    "N/A"); // Field90 already processed
            }
        }

        // 2. Load Original Transaction
        var originalTx = await _db.Transactions.FirstOrDefaultAsync(t => t.TraceId == request.OriginalTraceId, ct);
        
        var connectorId = originalTx?.ConnectorId ?? "SIMULATOR";
        var originalMti = originalTx?.RequestMti ?? "0100";
        var originalStan = originalTx?.Stan ?? "000000";
        var originalTime = originalTx?.CreatedOn ?? DateTimeOffset.UtcNow;
        
        // 3. Build Field 90
        var field90 = _field90Svc.BuildForConnector(connectorId, originalMti, originalStan, originalTime);

        // 4. Build ISO 0420 Message
        var iso = new IsoMessage { Mti = "0420" };
        var now = DateTimeOffset.UtcNow;
        var stan = Guid.NewGuid().ToString("N")[..6]; // We generate a new STAN for the advice

        iso.Set(7, now.ToString("MMddHHmmss"));
        iso.Set(11, stan);
        iso.Set(37, Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());
        
        if (originalTx != null)
        {
            if (!string.IsNullOrWhiteSpace(originalTx.ProcessingCode)) iso.Set(3, originalTx.ProcessingCode);
            if (!string.IsNullOrWhiteSpace(originalTx.Amount12)) iso.Set(4, originalTx.Amount12);
            if (!string.IsNullOrWhiteSpace(originalTx.Currency)) iso.Set(49, originalTx.Currency);
            if (!string.IsNullOrWhiteSpace(originalTx.TerminalId)) iso.Set(41, originalTx.TerminalId);
            if (!string.IsNullOrWhiteSpace(originalTx.MerchantId)) iso.Set(42, originalTx.MerchantId);
        }

        iso.Set(90, field90);
        iso.Set(64, _macSvc.ComputeMacHex("REVADV"));
        iso.Set(128, _macSvc.ComputeMacHex("REVADV128"));

        // 5. Create Reversal-Advice Transaction Entity
        var tx = new TransactionEntity
        {
            TraceId = request.TraceId,
            CorrelationId = request.OriginalTraceId,
            IdempotencyKey = request.IdempotencyKey,
            RequestMti = iso.Mti,
            RequestJson = JsonSerializer.Serialize(iso.Fields),
            Stan = stan,
            TxType = TransactionTypes.ReversalAdvice,
            OriginalTraceId = request.OriginalTraceId,
            Status = TransactionStatuses.Pending,
            Decision = "UNKNOWN",
            ConnectorId = connectorId,
            CreatedOn = now,
            UpdatedOn = now,
            InDoubt = false,
            ReversalStatus = "N/A"
        };

        _db.Transactions.Add(tx);

        if (originalTx != null)
        {
            originalTx.ReversalState = "REVERSAL_PENDING";
            originalTx.UpdatedOn = now;
        }

        await _db.SaveChangesAsync(ct);

        // 6. Send to Connector
        await _audit.LogAsync(request.TraceId, "OUT", iso, ct);
        await _publisher.PublishIsoAsync(request.TraceId, new { type = "sw.iso.sent", traceId = request.TraceId, mti = iso.Mti, connectorId, at = DateTimeOffset.UtcNow }, ct);

        var connector = _registry.Get(connectorId);
        IsoMessage resp;
        try
        {
            resp = await connector.AuthorizeAsync(iso, ct);
        }
        catch (Exception)
        {
            tx.Status = TransactionStatuses.InDoubt;
            tx.InDoubt = true;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        // 7. Process Response
        await _audit.LogAsync(request.TraceId, "IN", resp, ct);
        await _publisher.PublishIsoAsync(request.TraceId, new { type = "sw.iso.received", traceId = request.TraceId, mti = resp.Mti, connectorId, at = DateTimeOffset.UtcNow }, ct);

        var rc = resp.Fields.TryGetValue(39, out var code) ? code : "XX";
        var nextStatus = rc == "00" ? TransactionStatuses.Confirmed : TransactionStatuses.Declined;
        
        TransactionStateMachine.EnsureTransition(tx.TxType, tx.Status, nextStatus);

        tx.Status = nextStatus;
        tx.Decision = rc == "00" ? "APPROVED" : "DECLINED";
        tx.ResponseCode = rc;
        tx.UpdatedOn = DateTimeOffset.UtcNow;

        if (originalTx != null)
        {
            originalTx.ReversalState = rc == "00" ? "REVERSAL_CONFIRMED" : "REVERSAL_FAILED";
            originalTx.ReversalConfirmedOn = rc == "00" ? DateTimeOffset.UtcNow : null;
            originalTx.UpdatedOn = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await _publisher.PublishTxAsync(request.TraceId, new 
        { 
            type = "sw.tx.updated", 
            traceId = request.TraceId, 
            status = tx.Status, 
            decision = tx.Decision, 
            responseCode = rc, 
            connectorId, 
            updatedOn = DateTimeOffset.UtcNow, 
            originalTraceId = request.OriginalTraceId, 
            idempotencyKey = request.IdempotencyKey 
        }, ct);

        return new ReversalAdviceResult(
            request.TraceId,
            tx.Status,
            tx.Decision,
            rc,
            connectorId,
            request.IdempotencyKey,
            request.OriginalTraceId,
            field90
        );
    }
}
