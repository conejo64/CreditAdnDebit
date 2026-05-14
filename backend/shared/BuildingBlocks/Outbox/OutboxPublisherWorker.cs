using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Outbox;

public abstract class OutboxPublisherWorker : BackgroundService
{
    protected abstract Task<List<OutboxMessage>> GetPendingAsync(CancellationToken ct);
    protected abstract Task SaveAsync(OutboxMessage msg, CancellationToken ct);

    private readonly IEventBus _bus;
    private readonly ILogger _logger;

    protected OutboxPublisherWorker(IEventBus bus, ILogger logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            List<OutboxMessage> pending;
            try
            {
                pending = await GetPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox: error leyendo pendientes");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            foreach (var msg in pending)
            {
                try
                {
                    await _bus.PublishAsync(msg.Topic, msg.Key, msg.PayloadJson, stoppingToken);
                    msg.ProcessedOn = DateTimeOffset.UtcNow;
                }
                catch (Exception ex)
                {
                    msg.Attempts++;
                    msg.LastError = ex.Message;
                    _logger.LogError(ex, "Outbox: error publicando topic={Topic}", msg.Topic);
                }

                try
                {
                    await SaveAsync(msg, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox: error guardando estado");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}