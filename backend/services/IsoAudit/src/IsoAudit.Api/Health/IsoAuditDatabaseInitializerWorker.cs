using IsoSwitch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IsoAudit.Api.Health;

public sealed class IsoAuditDatabaseInitializerWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;
    private readonly IIsoAuditReadinessState _state;
    private readonly IOptions<IsoAuditReadinessOptions> _options;
    private readonly ILogger<IsoAuditDatabaseInitializerWorker> _logger;

    public IsoAuditDatabaseInitializerWorker(
        IServiceProvider serviceProvider,
        IHostEnvironment environment,
        IIsoAuditReadinessState state,
        IOptions<IsoAuditReadinessOptions> options,
        ILogger<IsoAuditDatabaseInitializerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
        _state = state;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.Value.StartupGracePeriod;
        _state.MarkStarting(IsoAuditReadinessChecks.Database);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(_options.Value.DatabaseTimeout);

                await using var scope = _serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
                var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) ?? false;
                if (_environment.IsDevelopment() || isInMemory)
                    await db.Database.EnsureCreatedAsync(timeoutCts.Token);
                else
                    await db.Database.MigrateAsync(timeoutCts.Token);

                _state.MarkReady(IsoAuditReadinessChecks.Database);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IsoAudit database readiness check failed");
                var expired = DateTimeOffset.UtcNow >= deadline;
                _state.MarkUnready(IsoAuditReadinessChecks.Database, expired ? "database unavailable" : "database starting");
                await DelayWithinBounds(stoppingToken);
            }
        }
    }

    private async Task DelayWithinBounds(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_options.Value.ProbeInterval, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}