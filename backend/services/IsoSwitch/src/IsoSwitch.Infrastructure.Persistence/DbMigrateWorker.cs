using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IsoSwitch.Infrastructure.Persistence;

public sealed class DbMigrateWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DbMigrateWorker> _logger;

    public DbMigrateWorker(IServiceProvider sp, ILogger<DbMigrateWorker> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
        _logger.LogInformation("Applying migrations for IsoSwitchDbContext...");
        await db.Database.MigrateAsync(stoppingToken);
        _logger.LogInformation("Migrations applied.");
    }
}
