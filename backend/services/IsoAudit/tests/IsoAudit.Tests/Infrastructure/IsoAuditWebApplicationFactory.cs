using IsoSwitch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IsoAudit.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory that boots IsoAudit.Api with:
/// - InMemory database
/// - Hosted services suppressed (Kafka consumer worker)
/// - A valid test key for Jwt:Key so startup validation passes
/// </summary>
public sealed class IsoAuditWebApplicationFactory : WebApplicationFactory<Program>
{
    // Valid non-placeholder test key (32+ chars, no forbidden substrings)
    internal const string TestJwtKey = "TestJwtKeyForIsoAuditServiceTests32Plus";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Inject valid test JWT settings so startup validation passes by default.
        // Issuer/Audience match CardVault's TokenService output (ADR-2, Task 2.2).
        builder.UseSetting("Jwt:Key", TestJwtKey);
        builder.UseSetting("Jwt:Issuer", "CardVault");
        builder.UseSetting("Jwt:Audience", "CardSwitch");

        builder.ConfigureTestServices(services =>
        {
            // Replace IsoSwitchDbContext (used by IsoAudit) with InMemory
            ReplaceWithInMemoryDb<IsoSwitchDbContext>(services, $"IsoAudit_Test_{Guid.NewGuid():N}");

            // Suppress all hosted services (Kafka consumer worker)
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var d in hostedServices) services.Remove(d);
        });
    }

    private static void ReplaceWithInMemoryDb<TContext>(IServiceCollection services, string dbName)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var toRemove = services
            .Where(d =>
                d.ServiceType == typeof(DbContextOptions<TContext>) ||
                d.ServiceType == typeof(DbContextOptions)           ||
                d.ServiceType == typeof(TContext))
            .ToList();
        foreach (var d in toRemove) services.Remove(d);

        services.AddSingleton(options);
        services.AddScoped<TContext>(sp =>
        {
            var opts = sp.GetRequiredService<DbContextOptions<TContext>>();
            return (TContext)Activator.CreateInstance(typeof(TContext), opts)!;
        });
    }
}
