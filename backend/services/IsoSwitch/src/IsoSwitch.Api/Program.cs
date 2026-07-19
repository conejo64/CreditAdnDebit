using BuildingBlocks.Kafka;
using IsoSwitch.Api;
using IsoSwitch.Application;
using IsoSwitch.Application.Config;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Messaging;
using IsoSwitch.Infrastructure.Consumers;
using IsoSwitch.Api.Iso8583;
using IsoSwitch.Api.Routing;
using IsoSwitch.Api.Security;
using Confluent.Kafka;
using IsoSwitch.Infrastructure.Persistence.Routing;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IsoSwitch.Api.Background;
using IsoSwitch.Api.Tcp;
using IsoSwitch.Api.Endpoints;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));
// v26 - OpenTelemetry (Tracing + Metrics)
var otelServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "IsoSwitch.Api";
var otelOtlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"]; // e.g., http://localhost:4317
var otelConsole = (builder.Configuration["OpenTelemetry:ConsoleExporter"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService(otelServiceName)).WithTracing(t =>
{
    t.AddSource("IsoSwitch").AddSource("BuildingBlocks.Kafka").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (!string.IsNullOrWhiteSpace(otelOtlpEndpoint))
        t.AddOtlpExporter(o => o.Endpoint = new Uri(otelOtlpEndpoint));
    if (otelConsole)
        t.AddConsoleExporter();
}).WithMetrics(m =>
{
    m.AddMeter("IsoSwitch.Metrics").AddAspNetCoreInstrumentation().AddRuntimeInstrumentation().AddMeter("BuildingBlocks.Kafka.Metrics").AddPrometheusExporter();
});
// ADR-4: CORS allowlist — origins driven by config (SEC-6). AllowAnyOrigin removed.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddEndpointsApiExplorer();
// ADR-1: TokenizationOptions with ValidateOnStart + custom placeholder validator
builder.Services.AddOptions<TokenizationOptions>()
    .BindConfiguration(TokenizationOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TokenizationOptions>, TokenizationOptionsValidator>();
builder.Services.AddSingleton<ITokenPanService, TokenPanService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSwaggerGen();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateLifetime = true
    };
});
builder.Services.AddAuthorization(options =>
{
    static bool RoleOrPerm(ClaimsPrincipal user, string permission, params string[] roles)
        => roles.Any(user.IsInRole) || user.HasClaim("perm", permission);

    options.AddPolicy(IsoSwitchAuthorizationPolicies.ViewSwitchMonitor, p => p.RequireAssertion(ctx =>
        RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.SwitchMonitorPermission, "Admin", "Auditor")));
    options.AddPolicy(IsoSwitchAuthorizationPolicies.OperateSwitch, p => p.RequireAssertion(ctx =>
        RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.SwitchOperatePermission, "Admin", "Operator")));
    options.AddPolicy(IsoSwitchAuthorizationPolicies.ManageSwitchRoutes, p => p.RequireAssertion(ctx =>
        RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.RoutingManagePermission, "Admin")));
    options.AddPolicy(IsoSwitchAuthorizationPolicies.ViewAudit, p => p.RequireAssertion(ctx =>
        RoleOrPerm(ctx.User, IsoSwitchAuthorizationPolicies.AuditViewPermission, "Admin", "Auditor")));
});
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IsoSwitch.Application.ApplicationMarker>());
// Postgres (switch db)
builder.Services.AddDbContext<IsoSwitchDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), b => b.MigrationsAssembly("IsoSwitch.Infrastructure.Persistence"));
    opt.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
builder.Services.AddSingleton<ISwitchEventPublisher, SwitchEventPublisher>();
builder.Services.AddScoped<IIsoAuditService, IsoAuditService>();
builder.Services.AddScoped<BinaryIsoAuditService>();
builder.Services.AddScoped<CatalogAuditPersistence>();
builder.Services.AddSingleton<Field90Service>();
builder.Services.AddHostedService<ReversalWorker>();
// IsoSimulatorOptions is registered unconditionally (pure config data, no side
// effects) so the `/simulator/options` minimal API endpoint can resolve it from
// DI in every environment; only the simulator's hosted services are dev-gated.
var simOpt = builder.Configuration.GetSection("IsoSimulator").Get<IsoSimulatorOptions>() ?? new IsoSimulatorOptions();
builder.Services.AddSingleton(simOpt);
// ISO simulator server (dev)
if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue("IsoSimulator:Enabled", true))
{
    builder.Services.AddHostedService<IsoSwitch.Infrastructure.SwitchIso8583.Net.IsoSimulatorServer>();
    // v27 - Demo Kafka consumer for PCI audit (trace correlation)
    if ((builder.Configuration["Kafka:Consumers:PciAuditEnabled"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddHostedService<PciAuditConsumer>();
    // v29 - Retry republisher (consumes <topic>.retry and republishes to original after backoff)
    if ((builder.Configuration["Kafka:RetryRepublisher:Enabled"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddHostedService(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<KafkaRetryRepublisherWorker>>();
            return new KafkaRetryRepublisherWorker(bootstrapServers: cfg["Kafka:BootstrapServers"] ?? "localhost:9092", groupId: cfg["Kafka:RetryRepublisher:GroupId"] ?? "isoswitch-retry-republisher", clientId: cfg["Kafka:ClientId"] ?? "IsoSwitch.Api", retryTopic: cfg["Kafka:Topics:PciAudit"] ?? "sw.audit.pci" + (cfg["Kafka:Retry:Suffix"] ?? ".retry"), logger: logger, signingSecret: cfg["Kafka:SigningSecret"], baseDelayMs: int.TryParse(cfg["Kafka:RetryRepublisher:BaseDelayMs"], out var bd) ? bd : 500, maxDelayMs: int.TryParse(cfg["Kafka:RetryRepublisher:MaxDelayMs"], out var md) ? md : 30000);
        });
    }
}

// ISO TCP client + connectors
// SEC-04/SEC-10: TcpIsoClientOptions.UseTls defaults to true; fail startup fast in
// non-Development environments when TLS is disabled for a non-loopback acquirer host.
builder.Services.AddOptions<TcpIsoClientOptions>()
    .BindConfiguration("IsoClient")
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TcpIsoClientOptions>, TcpIsoClientOptionsValidator>();
// ADR-3: inject ILogger<TcpIsoClient> — no raw ISO frame bytes reach any log sink (SEC-5)
// ADR-7: AllowInvalidCert is permitted only in Development; production always validates certs
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<TcpIsoClient>>();

    var opt = new TcpIsoClientOptions();
    cfg.GetSection("IsoClient").Bind(opt);

    // ADR-7: gate AllowInvalidCert — only honour the config value in Development
    opt.AllowInvalidCert = builder.Environment.IsDevelopment() && opt.AllowInvalidCert;

    return new TcpIsoClient(opt, logger);
});
builder.Services.AddScoped<RoutingEngine>();
builder.Services.AddScoped<IRoutingEngineV2, RoutingEngineV2>();
builder.Services.AddSingleton<IAcquirerConnector>(sp => new SimulatorConnector(sp.GetRequiredService<TcpIsoClient>(), sp.GetRequiredService<IsoSwitch.Infrastructure.SwitchIso8583.Iso.PackagerRegistry>()));
builder.Services.AddSingleton<IAcquirerConnector>(sp => new TcpGatewayConnector(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IsoSwitch.Infrastructure.SwitchIso8583.Iso.PackagerRegistry>(), sp.GetRequiredService<ILogger<TcpIsoClient>>()));
// Registry by ConnectorId
builder.Services.AddSingleton<ConnectorRegistry>();
builder.Services.AddSingleton<IsoSwitch.Infrastructure.SwitchIso8583.Iso.PackagerRegistry>();
builder.Services.AddSingleton<IsoSwitch.Infrastructure.SwitchIso8583.Iso.IMacService, IsoSwitch.Infrastructure.SwitchIso8583.Iso.MacService>();
// Kafka consumer to sync config/events from CardVault
var kafka = builder.Configuration.GetSection("Kafka");
builder.Services.AddHostedService(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ConfigSyncConsumer>>();
    return new ConfigSyncConsumer(bootstrapServers: kafka["BootstrapServers"] ?? "localhost:9092", groupId: kafka["GroupId"] ?? "isoswitch-config", clientId: kafka["ClientId"] ?? "isoswitch", sp: sp, logger: logger);
});
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    // Dev/test (or InMemory provider) uses EnsureCreated; prod applies migrations.
    var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) ?? false;
    var retryCount = 0;
    while (retryCount < 5)
    {
        try
        {
            logger.LogInformation("Attempting to apply migrations for IsoSwitch (Attempt {RetryCount}/5)...", retryCount + 1);
            if (app.Environment.IsDevelopment() || isInMemory)
                await db.Database.EnsureCreatedAsync();
            else
                await db.Database.MigrateAsync();
            logger.LogInformation("IsoSwitch migrations applied successfully.");
            break;
        }
        catch (Exception ex)
        {
            retryCount++;
            if (retryCount >= 5)
            {
                logger.LogCritical(ex, "Could not connect to IsoSwitch database after 5 attempts. Shutting down.");
                throw;
            }
            logger.LogWarning("IsoSwitch Database not ready yet, retrying in 5 seconds... ({Message})", ex.Message);
            await Task.Delay(5000);
        }
    }

    var audit = scope.ServiceProvider.GetRequiredService<CatalogAuditPersistence>();
    await BinRoutingStore.InitializeFromDbAsync(audit, CancellationToken.None);
    await IsoSwitch.Api.Tcp.PanMapStore.InitializeFromDbAsync(audit, CancellationToken.None);
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.MapPrometheusScrapingEndpoint("/metrics");
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { service = "IsoSwitch", status = "ok" }));

// Map Extracted Endpoints
app.MapTransactionEndpoints();
app.MapTransactionQueriesEndpoints();
app.MapCatalogEndpoints();
app.MapSimulatorEndpoints();
app.MapAuditEndpoints();

app.Run();

public sealed record SimPurchaseRequest(Guid AccountId, decimal Amount, string Network, string? Mti, string? Stan, string? Rrn, DateTimeOffset? PostedOn, string? ReasonCode);
// v42 - Preauth / Clearing simulation endpoints
public sealed record SimAuthRequest(Guid AccountId, decimal Amount, string Network, string? Mti, string? Stan, string? Rrn, string? OriginalDataElements90, string? MerchantId, string? MerchantCategory, DateTimeOffset? PostedOn);
public sealed record PanMapRequest(string Pan, Guid AccountId);
public sealed record TcpSendRequest(string FrameHex, string? Host, int? Port);
public sealed record PanMapTokenRequest(string TokenPan, string AccountId);
public sealed record TokenizePanRequest(string Pan);
public sealed record PanMapV51Request(string Pan, string AccountId);
public sealed record AuthorizeRequest(string TraceId, int Bin, decimal Amount, string Currency, string MerchantId, string TerminalId, string Stan, string? PinBlock, string? EmvTlv, // v16: optional ISO fields (demo). Avoid real PAN in production; prefer tokenization.
string? Pan, string? ExpiryYyMm, string? PosEntryMode, string? PosConditionCode, string? Track2, string? AdditionalAmounts54, string? Private60, string? Private61, string? Private62);
public sealed record ReversalRequest(string OriginalTraceId, string MerchantId, string TerminalId, string Currency, string? PinBlock, string? EmvTlv);
public sealed record SwitchAuthApprovedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? OriginalDataElements90, DateTimeOffset PostedOn);
public sealed record SwitchAuthReversedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? OriginalDataElements90, DateTimeOffset PostedOn);
public sealed record SwitchClearingPostedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? OriginalDataElements90, DateTimeOffset PostedOn);
public sealed record SwitchRefundPostedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, DateTimeOffset PostedOn);

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
public sealed record SwitchChargebackPostedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? ReasonCode, DateTimeOffset PostedOn);
