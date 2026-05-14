using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardVault.Infrastructure.Persistence.Outbox;

public sealed class EfOutboxPublisher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IEventBus _bus;
    private readonly ILogger<EfOutboxPublisher> _logger;

    public EfOutboxPublisher(IServiceProvider sp, IEventBus bus, ILogger<EfOutboxPublisher> logger)
    {
        _sp = sp;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();

                var pending = await db.OutboxMessages
                    .Where(x => x.ProcessedOn == null && x.Attempts < 10)
                    .OrderBy(x => x.OccurredOn)
                    .Take(50)
                    .ToListAsync(stoppingToken);

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
                        _logger.LogError(ex, "Outbox publish failed topic={Topic}", msg.Topic);
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EfOutboxPublisher loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}