using IsoSwitch.Application.Config;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IsoSwitch.Application.Features.Transactions.Commands.ReversalTransaction;

public class ReversalTransactionCommandHandler : IRequestHandler<ReversalTransactionCommand, ReversalTransactionResult>
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly IMacService _macSvc;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;

    public ReversalTransactionCommandHandler(
        IsoSwitchDbContext db,
        ConnectorRegistry registry,
        IMacService macSvc,
        ISwitchEventPublisher publisher,
        IIsoAuditService audit)
    {
        _db = db;
        _registry = registry;
        _macSvc = macSvc;
        _publisher = publisher;
        _audit = audit;
    }

    public async Task<ReversalTransactionResult> Handle(ReversalTransactionCommand request, CancellationToken ct)
    {
        // 1. Find Original Transaction
        var tx = await _db.Transactions.FirstOrDefaultAsync(x => x.TraceId == request.OriginalTraceId, ct);
        
        if (tx is null)
        {
            throw new InvalidOperationException("Original transaction not found");
        }

        if (tx.Status == "REVERSED")
        {
            return new ReversalTransactionResult(
                request.OriginalTraceId,
                tx.ResponseCode ?? "00",
                tx.Status,
                tx.Decision);
        }

        // 2. Build ISO 0400 Message
        var iso = new IsoMessage
        {
            Mti = "0400"
        };
        iso.Set(11, tx.Stan);
        iso.Set(41, request.TerminalId ?? tx.TerminalId ?? "N/A");
        iso.Set(42, request.MerchantId ?? tx.MerchantId ?? "N/A");
        iso.Set(49, request.Currency ?? tx.Currency ?? "USD");
        
        var now = DateTimeOffset.UtcNow;
        iso.Set(7, now.ToString("MMddHHmmss"));
        iso.Set(12, now.ToString("HHmmss"));
        iso.Set(13, now.ToString("MMdd"));
        iso.Set(37, Guid.NewGuid().ToString("N")[..12].ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(request.PinBlock))
            iso.Set(52, request.PinBlock.Trim());
        if (!string.IsNullOrWhiteSpace(request.EmvTlv))
            iso.Set(55, request.EmvTlv.Trim().ToUpperInvariant());

        iso.Set(64, _macSvc.ComputeMacHex("DEMO"));
        iso.Set(128, _macSvc.ComputeMacHex("DEMO128"));

        // 3. Send to Connector
        var connector = _registry.Get(tx.ConnectorId);
        var connectorId = tx.ConnectorId;
        var traceId = tx.TraceId;

        IsoMessage resp;
        try
        {
            resp = await connector.ReversalAsync(iso, ct);
        }
        catch (Exception)
        {
            tx.InDoubt = true;
            tx.Status = TransactionStatuses.InDoubt;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        // 4. Audit and Publish
        await _audit.LogAsync(traceId, "IN", resp, ct);
        await _publisher.PublishIsoAsync(traceId, new { type = "sw.iso.received", traceId, mti = resp.Mti, connectorId, at = DateTimeOffset.UtcNow }, ct);

        var rc = resp.Fields.TryGetValue(39, out var code) ? code : "XX";

        // 5. Update State
        tx.Status = "REVERSED";
        tx.ResponseCode = rc;
        tx.Decision = "REVERSED";
        tx.CompletedOn = DateTimeOffset.UtcNow;
        tx.UpdatedOn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _publisher.PublishTxAsync(traceId, new 
        { 
            type = "sw.tx.updated", 
            traceId, 
            status = tx.Status, 
            decision = tx.Decision, 
            responseCode = tx.ResponseCode, 
            connectorId = tx.ConnectorId, 
            updatedOn = DateTimeOffset.UtcNow 
        }, ct);

        return new ReversalTransactionResult(
            request.OriginalTraceId,
            rc,
            tx.Status,
            tx.Decision);
    }
}
