using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Outbox;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace CardVault.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory that boots CardVault.Api with:
/// - InMemory databases (CardVaultDbContext + IdentityAppDbContext)
/// - Hosted services suppressed (Kafka, Outbox, background workers)
/// - A test JWT signing key matching the appsettings.json defaults so
///   tokens minted by <see cref="GenerateJwt"/> are accepted by the app.
/// </summary>
public sealed class CardVaultWebApplicationFactory : WebApplicationFactory<Program>
{
    // Must match appsettings.json Jwt section
    private const string Issuer    = "CardVault";
    private const string Audience  = "CardSwitch";
    // Valid non-placeholder test key (32+ chars, passes JwtOptionsValidator SEC-2).
    private const string SigningKey = "TestSigningKeyForCardVaultIntegrationTests";

    // SEC-01: appsettings.Development.json no longer carries a live Vault:Keys value
    // (the previous k1/k2 values were purged from committed config). VaultCrypto still
    // requires at least one 32-byte AES-256-GCM key to construct, so tests supply their
    // own test-only keys here — analogous to the Jwt:SigningKey override above. Key ids
    // "k1"/"k2" are kept (test-only random values, NOT the previously leaked material)
    // because several existing integration tests target `?keyId=k1` on the HTTP rotate
    // endpoint and construct VaultOptions with "k1"/"k2" directly.
    private const string TestVaultKeyK1B64 = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string TestVaultKeyK2B64 = "IB8eHRwbGhkYFxYVFBMSERAPDg0MCwoJCAcGBQQDAgE=";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Provide a valid signing key so startup secret validation (SEC-2) passes.
        builder.UseSetting("Jwt:SigningKey", SigningKey);

        // Provide test-only vault keys so VaultCrypto can construct (SEC-01: no live
        // key ships in committed config anymore).
        builder.UseSetting("Vault:ActiveKeyId", "k2");
        builder.UseSetting("Vault:Keys:k1", TestVaultKeyK1B64);
        builder.UseSetting("Vault:Keys:k2", TestVaultKeyK2B64);

        // Provide test-only connection strings so RequiredConnectionStringsOptionsValidator
        // (SEC-9 fail-fast) passes by default. The DbContexts themselves are replaced with
        // InMemory providers below, so these values are never actually connected to.
        builder.UseSetting("ConnectionStrings:Postgres", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("ConnectionStrings:SqlServerIdentity", "Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=true");

        builder.ConfigureTestServices(services =>
        {
            // ── Replace CardVaultDbContext with InMemory ──────────────────
            ReplaceWithInMemoryDb<CardVaultDbContext>(services, $"CardVault_Test_{Guid.NewGuid():N}");

            // ── Replace IdentityAppDbContext with InMemory ────────────────
            ReplaceWithInMemoryDb<IdentityAppDbContext>(services, $"Identity_Test_{Guid.NewGuid():N}");

            // ── Suppress all hosted services (Kafka, outbox, background workers) ──
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var d in hostedServices) services.Remove(d);

            // ── Replace the Kafka event bus with a no-op stub ─────────────
            var eventBusDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IEventBus));
            if (eventBusDescriptor is not null) services.Remove(eventBusDescriptor);
            services.AddSingleton<IEventBus>(new NullEventBus());

            // ── Override JWT validation to use the test signing key ───────
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opt =>
            {
                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidIssuer              = Issuer,
                    ValidateAudience         = true,
                    ValidAudience            = Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    ValidateLifetime         = true,
                    ClockSkew                = TimeSpan.FromMinutes(5)
                };
            });
        });
    }

    /// <summary>
    /// Mints a signed JWT accepted by the test host.
    /// </summary>
    /// <param name="roles">Role claim values (e.g., "Admin", "Operator", "Auditor").</param>
    /// <param name="extraClaims">Additional claims, e.g. ("perm","collections:view").</param>
    public string GenerateJwt(
        IEnumerable<string> roles,
        IEnumerable<(string type, string value)>? extraClaims = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,  Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, $"test+{Guid.NewGuid():N}@test.local"),
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (extraClaims is not null)
            foreach (var (type, value) in extraClaims)
                claims.Add(new Claim(type, value));

        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token       = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── Minimal no-op IEventBus ───────────────────────────────────────────────
    private sealed class NullEventBus : IEventBus
    {
        public Task PublishAsync(string topic, string key, string payloadJson, CancellationToken ct)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Removes all service descriptors related to a DbContext so the InMemory
    /// replacement is the sole provider (avoids dual-provider EF exception).
    /// Replaces with a pre-built options instance (no AddDbContext re-registration)
    /// to ensure EF's ServiceProviderCache only sees one provider.
    /// </summary>
    private static void ReplaceWithInMemoryDb<TContext>(IServiceCollection services, string dbName)
        where TContext : DbContext
    {
        // Build the replacement options BEFORE touching the service collection
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Remove all EF-related registrations for TContext
        // (options, typed options, context itself, and internal configurations)
        var toRemove = services
            .Where(d =>
                d.ServiceType == typeof(DbContextOptions<TContext>) ||
                d.ServiceType == typeof(DbContextOptions)           ||
                d.ServiceType == typeof(TContext))
            .ToList();
        foreach (var d in toRemove) services.Remove(d);

        // Re-register using the pre-built options instance — no provider-specific
        // DI infrastructure is added this way, just a concrete options object.
        services.AddSingleton(options);
        services.AddScoped<TContext>(sp =>
        {
            var opts = sp.GetRequiredService<DbContextOptions<TContext>>();
            return (TContext)Activator.CreateInstance(typeof(TContext), opts)!;
        });
    }
}
