using IsoSwitch.Application.Config;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IsoSwitch.Application.Features.Transactions.Commands.CaptureTransaction;

public class CaptureTransactionCommandHandler : IRequestHandler<CaptureTransactionCommand, CaptureTransactionResult>
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly IRoutingEngineV2 _router;
    private readonly IMacService _macSvc;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;

    public CaptureTransactionCommandHandler(
        IsoSwitchDbContext db,
        ConnectorRegistry registry,
        IRoutingEngineV2 router,
        IMacService macSvc,
        ISwitchEventPublisher publisher,
        IIsoAuditService audit)
    {
        _db = db;
        _registry = registry;
        _router = router;
        _macSvc = macSvc;
        _publisher = publisher;
        _audit = audit;
    }

    public async Task<CaptureTransactionResult> Handle(CaptureTransactionCommand request, CancellationToken ct)
    {
        // 1. Idempotency Check
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == request.IdempotencyKey && t.TxType == TransactionTypes.Capture, ct);

            if (existing is not null)
            {
                return new CaptureTransactionResult(
                    existing.TraceId,
                    existing.Status,
                    existing.Decision,
                    existing.ResponseCode,
                    existing.ConnectorId,
                    existing.IdempotencyKey);
            }
        }

        // 2. Resolve Connector via Router
        var decision = await _router.ResolveAsync(request.Bin, null, null, "AUTH", ct);
        var connectorId = decision.ConnectorId;

        // 3. Build ISO Message
        var iso = new IsoMessage { Mti = "0200" };
        iso.Set(3, "000000"); // Standard capture processing code
        iso.Set(4, ((int)(request.Amount * 100)).ToString());
        iso.Set(11, request.Stan);
        iso.Set(41, request.TerminalId);
        iso.Set(42, request.MerchantId);
        iso.Set(49, request.Currency);

        if (!string.IsNullOrWhiteSpace(request.Pan)) iso.Set(2, request.Pan.Trim());
        if (!string.IsNullOrWhiteSpace(request.ExpiryYyMm)) iso.Set(14, request.ExpiryYyMm.Trim());
        if (!string.IsNullOrWhiteSpace(request.PosEntryMode)) iso.Set(22, request.PosEntryMode.Trim());
        if (!string.IsNullOrWhiteSpace(request.PosConditionCode)) iso.Set(25, request.PosConditionCode.Trim());
        if (!string.IsNullOrWhiteSpace(request.Track2)) iso.Set(35, request.Track2.Trim());
        if (!string.IsNullOrWhiteSpace(request.AdditionalAmounts54)) iso.Set(54, request.AdditionalAmounts54.Trim());
        if (!string.IsNullOrWhiteSpace(request.Private60)) iso.Set(60, request.Private60.Trim());
        if (!string.IsNullOrWhiteSpace(request.Private61)) iso.Set(61, request.Private61.Trim());
        if (!string.IsNullOrWhiteSpace(request.Private62)) iso.Set(62, request.Private62.Trim());

        var now = DateTimeOffset.UtcNow;
        iso.Set(7, now.ToString("MMddHHmmss"));
        iso.Set(12, now.ToString("HHmmss"));
        iso.Set(13, now.ToString("MMdd"));
        iso.Set(37, Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(request.PinBlock)) iso.Set(52, request.PinBlock.Trim());
        if (!string.IsNullOrWhiteSpace(request.EmvTlv)) iso.Set(55, request.EmvTlv.Trim().ToUpperInvariant());

        iso.Set(64, _macSvc.ComputeMacHex("CAPTURE"));
        iso.Set(128, _macSvc.ComputeMacHex("CAPTURE128"));

        var requestJson = JsonSerializer.Serialize(iso.Fields);

        // 4. Persistence - Initial State
        var tx = new TransactionEntity
        {
            TraceId = request.TraceId,
            CorrelationId = request.TraceId,
            IdempotencyKey = request.IdempotencyKey,
            RequestMti = iso.Mti,
            RequestJson = requestJson, // Required field
            Stan = request.Stan,
            TxType = TransactionTypes.Capture,
            Status = TransactionStatuses.Pending,
            Decision = "UNKNOWN",
            ConnectorId = connectorId,
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow,
            InDoubt = false,
            ReversalStatus = "N/A",
            ProcessingCode = iso.Fields.TryGetValue(3, out var pc) ? pc : null,
            Amount12 = iso.Fields.TryGetValue(4, out var a12) ? a12 : null,
            Currency = iso.Fields.TryGetValue(49, out var c49) ? c49 : null,
            TerminalId = iso.Fields.TryGetValue(41, out var t41) ? t41 : null,
            MerchantId = iso.Fields.TryGetValue(42, out var m42) ? m42 : null,
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);

        // 5. Send to Connector
        var connector = _registry.Get(connectorId);
        await _audit.LogAsync(request.TraceId, "OUT", iso, ct);
        await _publisher.PublishIsoAsync(request.TraceId, new { type = "sw.iso.sent", traceId = request.TraceId, mti = iso.Mti, connectorId, at = DateTimeOffset.UtcNow }, ct);

        IsoMessage resp;
        try
        {
            resp = await connector.AuthorizeAsync(iso, ct);
        }
        catch (Exception)
        {
            tx.InDoubt = true;
            tx.Status = TransactionStatuses.InDoubt;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        // 6. Handle Response
        await _audit.LogAsync(request.TraceId, "IN", resp, ct);
        await _publisher.PublishIsoAsync(request.TraceId, new { type = "sw.iso.received", traceId = request.TraceId, mti = resp.Mti, connectorId, at = DateTimeOffset.UtcNow }, ct);

        var rc = resp.Fields.TryGetValue(39, out var code) ? code : "XX";
        var nextStatus = rc == "00" ? TransactionStatuses.Captured : TransactionStatuses.Declined;
        
        TransactionStateMachine.EnsureTransition(tx.TxType, tx.Status, nextStatus);

        tx.Status = nextStatus;
        tx.Decision = rc == "00" ? "APPROVED" : "DECLINED";
        tx.ResponseCode = rc;
        tx.UpdatedOn = DateTimeOffset.UtcNow;

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
            idempotencyKey = request.IdempotencyKey 
        }, ct);

        return new CaptureTransactionResult(
            request.TraceId,
            tx.Status,
            tx.Decision,
            tx.ResponseCode,
            tx.ConnectorId,
            tx.IdempotencyKey);
    }
}
