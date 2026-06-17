using CardVault.Application.Services.Notifications;

namespace CardVault.Api.Background;

public sealed class NotificationDispatcherWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<NotificationDispatcherWorker> _logger;

    public NotificationDispatcherWorker(IServiceProvider sp, ILogger<NotificationDispatcherWorker> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationDispatcherWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                await dispatcher.DispatchBatchAsync(50, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification dispatcher loop failed.");
            }

            if (stoppingToken.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
