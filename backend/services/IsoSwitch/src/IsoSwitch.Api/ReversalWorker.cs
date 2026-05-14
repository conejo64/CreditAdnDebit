using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IsoSwitch.Api;

public sealed class ReversalWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ReversalWorker> _logger;

    public ReversalWorker(IServiceProvider sp, ILogger<ReversalWorker> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
                var registry = scope.ServiceProvider.GetRequiredService<ConnectorRegistry>();
                var publisher = scope.ServiceProvider.GetRequiredService<ISwitchEventPublisher>();

                var now = DateTimeOffset.UtcNow;
                var due = await db.Transactions
                    .Where(t => t.InDoubt && t.ReversalStatus == "PENDING" && t.ReversalAttempts < 3 && t.ReversalScheduledOn != null && t.ReversalScheduledOn <= now)
                    .OrderBy(t => t.CreatedOn)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var tx in due)
                {
                    tx.ReversalAttemptedOn = DateTimeOffset.UtcNow;
                    tx.ReversalAttempts += 1;
                    tx.ReversalStatus = "SENT";
                    await db.SaveChangesAsync(stoppingToken);
                        await publisher.PublishTxAsync(tx.TraceId, new { type = "sw.tx.updated", traceId = tx.TraceId, status = tx.Status, decision = tx.Decision, responseCode = tx.ResponseCode, connectorId = tx.ConnectorId, updatedOn = DateTimeOffset.UtcNow }, stoppingToken);

                    try
                    {
                        var iso = new IsoMessage { Mti = "0400" };
                        iso.Set(11, tx.Stan);
                        // For demo, rely on simulator accepting without extra fields
                        iso.Set(41, "T123");
                        iso.Set(42, "M123");
                        iso.Set(49, "USD");

                        var connector = registry.Get(tx.ConnectorId);
                        var resp = await connector.ReversalAsync(iso, stoppingToken);
                        var rc = resp.Fields.TryGetValue(39, out var code) ? code : "XX";

                        tx.ReversalStatus = rc == "00" ? "CONFIRMED" : "FAILED";
                        tx.Status = "REVERSED";
                        tx.ResponseCode = rc;
                        tx.Decision = "REVERSED";
                        tx.CompletedOn = DateTimeOffset.UtcNow;

                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Auto reversal failed for traceId={TraceId}", tx.TraceId);
                        tx.ReversalStatus = "PENDING";
                        var delay = tx.ReversalAttempts switch { 1 => 10, 2 => 30, _ => 60 };
                        tx.ReversalScheduledOn = DateTimeOffset.UtcNow.AddSeconds(delay);
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReversalWorker loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}