using BuildingBlocks.Security;
using IsoSwitch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace IsoSwitch.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory that boots IsoSwitch.Api with:
/// - InMemory database
/// - Hosted services suppressed (Kafka, ISO simulator, background workers)
/// - A valid test secret for Tokenization:Secret so startup validation passes
/// - A test-only ES256 public key for Jwt:PublicKeyPem (G1-T5). The matching private key
///   stays in the test process so tests can mint tokens shaped like CardVault's; the host
///   itself never sees it.
/// </summary>
public sealed class IsoSwitchWebApplicationFactory : WebApplicationFactory<Program>
{
    // Valid non-placeholder test secrets (32+ chars, no forbidden substrings)
    internal const string TestTokenizationSecret = "TestTokenizationSecretForIsoSwitch32Plus";
    internal const string TestAdminApiKey        = "TestAdminApiKeyForIsoSwitchTests32PlusChars";
    internal const string Issuer                 = "CardVault";
    internal const string Audience               = "CardSwitch";

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

        // Inject valid test secrets so startup validation passes by default
        builder.UseSetting("Tokenization:Secret", TestTokenizationSecret);
        builder.UseSetting("Jwt:PublicKeyPem", PublicKeyPem);
        // SEC-05/SEC-11: operator-supplied admin API key fail-fast validation
        builder.UseSetting("Admin:ApiKey", TestAdminApiKey);

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
