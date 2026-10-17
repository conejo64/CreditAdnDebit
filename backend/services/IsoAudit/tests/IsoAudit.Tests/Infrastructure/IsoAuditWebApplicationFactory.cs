using BuildingBlocks.Security;
using IsoSwitch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace IsoAudit.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory that boots IsoAudit.Api with:
/// - InMemory database
/// - Hosted services suppressed (Kafka consumer worker)
/// - A test-only ES256 public key for Jwt:PublicKeyPem (G1-T5). The matching private key
///   stays in the test process so tests can mint tokens shaped like CardVault's; the host
///   itself never sees it.
/// </summary>
public sealed class IsoAuditWebApplicationFactory : WebApplicationFactory<Program>
{
    internal const string Issuer   = "CardVault";
    internal const string Audience = "CardSwitch";

    // G1-T5: one ephemeral P-256 pair per test process, shared by every factory instance.
    private static readonly EcdsaKeyPairPem TestKeyPair = EcdsaKeyMaterial.GenerateKeyPair();
    internal static string PrivateKeyPem => TestKeyPair.PrivateKeyPem;
    internal static string PublicKeyPem  => TestKeyPair.PublicKeyPem;

    /// <summary>Signing credentials matching the public key the test host validates with.</summary>
    internal static SigningCredentials CreateSigningCredentials() => CreateSigningCredentials(PrivateKeyPem);

    internal static SigningCredentials CreateSigningCredentials(string privateKeyPem)
    {
        var ecdsa = EcdsaKeyMaterial.ImportPrivateKey(privateKeyPem);
        var key = new ECDsaSecurityKey(ecdsa) { KeyId = EcdsaKeyMaterial.ComputeKeyId(ecdsa) };
        return new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Inject valid test JWT settings so startup validation passes by default.
        // Issuer/Audience match CardVault's TokenService output (ADR-2, Task 2.2).
        builder.UseSetting("Jwt:PublicKeyPem", PublicKeyPem);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);

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
