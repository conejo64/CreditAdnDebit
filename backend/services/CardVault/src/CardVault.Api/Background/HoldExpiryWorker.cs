using CardVault.Application.Services;

namespace CardVault.Api.Background;

/// <summary>
/// Periodically scans and expires active holds where ExpiresOn < now.
/// For demo: runs every 60 seconds.
/// In production: run via scheduled job (Hangfire/Quartz/Kubernetes CronJob).
/// </summary>
public sealed class HoldExpiryWorker : BackgroundService
{
    private readonly ILogger<HoldExpiryWorker> _logger;
    private readonly IServiceProvider _sp;

    public HoldExpiryWorker(ILogger<HoldExpiryWorker> logger, IServiceProvider sp)
    {
        _logger = logger;
        _sp = sp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HoldExpiryWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<HoldMaintenanceService>();
                var expired = await svc.ExpireDueHoldsAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (expired > 0)
                    _logger.LogInformation("Expired holds: {Count}", expired);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hold expiry run failed (non-fatal).");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        _logger.LogInformation("HoldExpiryWorker stopped.");
    }
}
