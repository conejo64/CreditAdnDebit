using BuildingBlocks.Security;
using IsoAudit.Tests.Infrastructure;
using IsoSwitch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace IsoAudit.Tests.Security;

/// <summary>
/// SEC-4 / G1-T5: IsoAudit validates issuer, audience and the ES256 signature on every
/// protected endpoint, gates HTTPS metadata on environment, and accepts nothing signed with
/// a symmetric secret or with a private key other than CardVault's.
/// </summary>
public class JwtHardeningTests
{
    private const string CorrectIssuer = "CardVault";
    private const string CorrectAudience = "CardSwitch";
    private const string AuditScope = "audit.read";

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Mints a JWT with the given credentials (the test issuer key by default). Caller
    /// supplies issuer and audience so wrong-issuer / wrong-audience tests can exercise
    /// rejection paths.
    /// </summary>
    private static string MintToken(
        string issuer,
        string audience,
        SigningCredentials? credentials = null)
    {
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("scope", AuditScope)],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials ?? IsoAuditWebApplicationFactory.CreateSigningCredentials());

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<HttpStatusCode> CallLogsAsync(string token)
    {
        await using var factory = new IsoAuditWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/audit/logs");
        return response.StatusCode;
    }

    // ── Task 2.1a — wrong issuer returns 401 ────────────────────────────────

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        var status = await CallLogsAsync(MintToken(issuer: "wrong-issuer", audience: CorrectAudience));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // ── Task 2.1b — wrong audience returns 401 ──────────────────────────────

    [Fact]
    public async Task WrongAudience_Returns401()
    {
        var status = await CallLogsAsync(MintToken(issuer: CorrectIssuer, audience: "wrong-audience"));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // ── G1-T5 — the issuer key is accepted; anything else is not ────────────

    [Fact]
    public async Task TokenSignedWithTheIssuerPrivateKey_Returns200()
    {
        var status = await CallLogsAsync(MintToken(CorrectIssuer, CorrectAudience));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Hs256TokenSignedWithTheRetiredSharedSecret_Returns401()
    {
        var hmac = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("TestJwtKeyForIsoAuditServiceTests32Plus"));
        var token = MintToken(CorrectIssuer, CorrectAudience, new SigningCredentials(hmac, SecurityAlgorithms.HmacSha256));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallLogsAsync(token));
    }

    [Fact]
    public async Task Es256TokenSignedWithADifferentPrivateKey_Returns401()
    {
        var stranger = IsoAuditWebApplicationFactory.CreateSigningCredentials(EcdsaKeyMaterial.GenerateKeyPair().PrivateKeyPem);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallLogsAsync(MintToken(CorrectIssuer, CorrectAudience, stranger)));
    }

    // ── Task 2.1c — Development env → RequireHttpsMetadata=false ────────────

    [Fact]
    public void DevelopmentEnv_RequireHttpsMetadata_IsFalse()
    {
        // IsoAuditWebApplicationFactory already forces env=Development
        using var factory = new IsoAuditWebApplicationFactory();
        factory.CreateClient(); // trigger host build

        // IOptionsSnapshot is scoped — resolve via a service scope
        using var scope = factory.Services.CreateScope();
        var jwtOpts = scope.ServiceProvider
                           .GetRequiredService<IOptionsSnapshot<JwtBearerOptions>>()
                           .Get(JwtBearerDefaults.AuthenticationScheme);

        // In Development environment, RequireHttpsMetadata must be false
        Assert.False(jwtOpts.RequireHttpsMetadata);
    }

    // ── Task 2.1d — Production env → RequireHttpsMetadata=true ─────────────

    [Fact]
    public void ProductionEnv_RequireHttpsMetadata_IsTrue()
    {
        // Use a factory variant that sets env to Production AND patches the
        // DB step so InMemory doesn't try to run migrations.
        // Program.cs must guard MigrateAsync with db.Database.IsInMemory()
        // for this test to pass (done in Task 2.3 GREEN).
        using var factory = new ProductionIsoAuditWebApplicationFactory();
        factory.CreateClient(); // trigger host build

        using var scope = factory.Services.CreateScope();
        var jwtOpts = scope.ServiceProvider
                           .GetRequiredService<IOptionsSnapshot<JwtBearerOptions>>()
                           .Get(JwtBearerDefaults.AuthenticationScheme);

        // In Production environment, RequireHttpsMetadata must be true
        Assert.True(jwtOpts.RequireHttpsMetadata);
    }
}

/// <summary>
/// Factory for testing Production-environment behaviour.
/// Sets ASPNETCORE_ENVIRONMENT=Production and replaces the DbContext with
/// InMemory so Program.cs's migration path is compatible
/// (Program.cs must guard MigrateAsync with db.Database.IsInMemory()).
/// </summary>
internal sealed class ProductionIsoAuditWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        // Provide valid JWT settings so startup validation passes
        builder.UseSetting("Jwt:PublicKeyPem", IsoAuditWebApplicationFactory.PublicKeyPem);
        builder.UseSetting("Jwt:Issuer", "CardVault");
        builder.UseSetting("Jwt:Audience", "CardSwitch");
        builder.UseSetting("ConnectionStrings:IsoSwitchDb", IsoAuditWebApplicationFactory.TestIsoSwitchDbConnectionString);

        builder.ConfigureTestServices(services =>
        {
            // Replace DbContext with InMemory (same as base factory)
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<IsoSwitchDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions)                      ||
                    d.ServiceType == typeof(IsoSwitchDbContext))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            var opts = new DbContextOptionsBuilder<IsoSwitchDbContext>()
                .UseInMemoryDatabase($"IsoAudit_Prod_Test_{Guid.NewGuid():N}")
                .Options;
            services.AddSingleton(opts);
            services.AddScoped<IsoSwitchDbContext>(sp =>
            {
                var o = sp.GetRequiredService<DbContextOptions<IsoSwitchDbContext>>();
                return new IsoSwitchDbContext(o);
            });

            // Suppress hosted services (Kafka consumer worker)
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var d in hostedServices) services.Remove(d);
        });
    }
}
