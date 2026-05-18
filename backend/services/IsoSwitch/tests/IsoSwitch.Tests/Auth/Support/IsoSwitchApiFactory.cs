using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace IsoSwitch.Tests.Auth.Support;

/// <summary>
/// WebApplicationFactory for endpoint-level auth boundary tests.
///
/// Design choices (task 2.4 — never weaken production auth wiring):
/// - Replaces only EF Core (InMemory) and background workers that need external infra.
/// - The production authentication + authorization middleware runs unchanged.
/// - JWT validation is post-configured to use a fixed test signing key so the real
///   JWT bearer pipeline validates test tokens the same way it validates production tokens.
/// - Kafka consumers and TCP background services are removed to keep tests hermetic.
/// </summary>
public sealed class IsoSwitchApiFactory : WebApplicationFactory<Program>
{
    internal const string TestIssuer = "IsoSwitch.Test";
    internal const string TestAudience = "IsoSwitch.Test.Audience";
    internal const string TestSigningKey = "IsoSwitchTestSigningKey-At-Least-32-Chars!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IsoSimulator:Enabled"] = "false",
                ["Kafka:Consumers:PciAuditEnabled"] = "false",
                ["Kafka:RetryRepublisher:Enabled"]  = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Post-configure JWT bearer to use the test signing key.
            // This runs AFTER AddJwtBearer in Program.cs and overrides validation parameters.
            // Production auth wiring (middleware pipeline, policies, authorization handlers) 
            // is completely untouched — only the token validation key is replaced.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    RoleClaimType = ClaimTypes.Role,
                };
            });

            // Replace Postgres DbContext with InMemory so no real DB is needed.
            // Remove ALL descriptors that configure IsoSwitchDbContext — including the
            // EF-internal IDbContextOptionsConfiguration<T> that carries the Npgsql wiring.
            var toRemove = services
                .Where(d => d.ServiceType.FullName?.Contains("IsoSwitchDbContext") == true
                         || d.ServiceType == typeof(IsoSwitchDbContext)
                         || d.ServiceType == typeof(DbContextOptions<IsoSwitchDbContext>))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<IsoSwitchDbContext>(opt =>
                opt.UseInMemoryDatabase("IsoSwitch-Test-" + Guid.NewGuid()));

            // IsoSimulatorOptions is only registered in Development; add a stub so the
            // /api/simulator/options endpoint can resolve its parameter at startup.
            if (!services.Any(d => d.ServiceType == typeof(IsoSimulatorOptions)))
                services.AddSingleton(new IsoSimulatorOptions());

            // Remove hosted services that need real Kafka / TCP / DB migrations.
            RemoveHostedService(services, "ReversalWorker");
            RemoveHostedService(services, "ConfigSyncConsumer");
            RemoveHostedService(services, "DbMigrateWorker");
            RemoveHostedService(services, "IsoSimulatorServer");
            RemoveHostedService(services, "PciAuditConsumer");
            RemoveHostedService(services, "KafkaRetryRepublisherWorker");
        });
    }

    /// <summary>
    /// Creates an HttpClient with a JWT that only carries the specified role.
    /// Use this to test 403 scenarios: the user is authenticated but lacks the right role.
    /// </summary>
    public HttpClient CreateClientWithRole(string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(role: role));
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with a JWT that carries the specified permission claim (perm).
    /// </summary>
    public HttpClient CreateClientWithPermission(string permission)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(permission: permission));
        return client;
    }

    private static string CreateToken(string? role = null, string? permission = null)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, "test-user"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (permission is not null)
            claims.Add(new Claim("perm", permission));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void RemoveHostedService(IServiceCollection services, string typeName)
    {
        var toRemove = services
            .Where(d => d.ImplementationType?.Name == typeName
                     || d.ImplementationFactory?.Method.ReturnType.Name == typeName)
            .ToList();

        foreach (var descriptor in toRemove)
            services.Remove(descriptor);
    }
}

