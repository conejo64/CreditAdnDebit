using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardVault.Api.Vault;

public sealed class VaultStartupInitializer : IHostedService
{
    private readonly ILogger<VaultStartupInitializer> _log;
    private readonly IServiceProvider _sp;

    public VaultStartupInitializer(ILogger<VaultStartupInitializer> log, IServiceProvider sp)
    {
        _log = log;
        _sp = sp;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<VaultSettingsStore>();
        var crypto = scope.ServiceProvider.GetRequiredService<VaultCrypto>();

        var active = await store.GetActiveKeyIdAsync(cancellationToken);
        crypto.SetActiveKeyId(active);

        _log.LogInformation("Vault initialized with ActiveKeyId={ActiveKeyId}", active);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}