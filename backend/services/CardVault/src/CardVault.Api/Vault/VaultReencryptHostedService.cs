using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardVault.Api.Vault;

public sealed class VaultReencryptHostedService : BackgroundService
{
    private readonly ILogger<VaultReencryptHostedService> _log;
    private readonly IServiceProvider _sp;
    private readonly VaultJobOptions _opt;

    public VaultReencryptHostedService(ILogger<VaultReencryptHostedService> log, IServiceProvider sp, VaultJobOptions opt)
    {
        _log = log;
        _sp = sp;
        _opt = opt;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _log.LogInformation("Vault re-encrypt job disabled");
            return;
        }

        _log.LogInformation("Vault re-encrypt job enabled: every {Minutes}m, batch={BatchSize}", _opt.IntervalMinutes, _opt.BatchSize);

        var delay = TimeSpan.FromMinutes(Math.Max(1, _opt.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<TokenVaultService>();

                await svc.ReEncryptBatchAsync(_opt.BatchSize, "system-job", "job", stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Vault re-encrypt job failed");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch { /* ignored */ }
        }
    }
}

public sealed class VaultJobOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    public int BatchSize { get; set; } = 200;
}