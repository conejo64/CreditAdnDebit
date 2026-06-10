using IsoSwitch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IsoSwitch.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory that boots IsoSwitch.Api with:
/// - InMemory database
/// - Hosted services suppressed (Kafka, ISO simulator, background workers)
/// - A valid test secret for Tokenization:Secret so startup validation passes
/// - A valid test signing key for Jwt:SigningKey
/// </summary>
public sealed class IsoSwitchWebApplicationFactory : WebApplicationFactory<Program>
{
    // Valid non-placeholder test secrets (32+ chars, no forbidden substrings)
    internal const string TestTokenizationSecret = "TestTokenizationSecretForIsoSwitch32Plus";
    internal const string TestJwtSigningKey      = "TestJwtSigningKeyForIsoSwitchTests32Plus";
    internal const string Issuer                 = "CardVault";
    internal const string Audience               = "CardSwitch";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Inject valid test secrets so startup validation passes by default
        builder.UseSetting("Tokenization:Secret", TestTokenizationSecret);
        builder.UseSetting("Jwt:SigningKey", TestJwtSigningKey);

        builder.ConfigureTestServices(services =>
        {
            // Replace IsoSwitchDbContext with InMemory
            ReplaceWithInMemoryDb<IsoSwitchDbContext>(services, $"IsoSwitch_Test_{Guid.NewGuid():N}");

            // Suppress all hosted services (Kafka, ISO simulator, background workers)
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
