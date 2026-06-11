using Confluent.Kafka;
using IsoAudit.Api.Security;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ADR-1: JwtOptions with ValidateOnStart + custom placeholder validator (SEC-3)
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

// v55: JWT auth for audit read endpoints.
// Key is read from configuration here; ValidateOnStart (above) guarantees the
// host refuses to start unless Jwt:Key passes JwtOptionsValidator (SEC-3), so
// the bearer can never serve with an invalid key.
// TODO(slice-2): fold this bearer setup into IOptions<JwtOptions> when issuer/
// audience validation is enabled.
var jwtKeyBytes = System.Text.Encoding.UTF8.GetBytes(
    builder.Configuration["Jwt:Key"] ?? string.Empty);
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(jwtKeyBytes)
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("audit.read", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(c => (c.Type == "scope" || c.Type == "scp") && (c.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("audit.read") ?? false))
            || ctx.User.IsInRole("Admin")));
});


builder.Services.AddDbContext<IsoSwitchDbContext>(opt =>
{
    var cs = builder.Configuration.GetConnectionString("IsoSwitchDb") ??
             "Host=localhost;Port=5432;Database=isoswitch;Username=postgres;Password=postgres";
    opt.UseNpgsql(cs);
    opt.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// Kafka consumer worker
builder.Services.AddHostedService<IsoAuditConsumerWorker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
    // Dev/test uses EnsureCreated (InMemory-compatible); prod applies migrations.
    if (app.Environment.IsDevelopment())
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { ok = true, service = "IsoAudit.Api" }));

app.MapGet("/api/audit/logs", async (int? take, IsoSwitchDbContext db, CancellationToken ct) =>
{
    var t = Math.Clamp(take ?? 50, 1, 500);
    var logs = await db.IsoMessageLogs
        .OrderByDescending(x => x.Id)
        .Take(t)
        .Select(x => new { x.Id, x.TraceId, x.Direction, x.Mti, x.FieldsJson, x.CreatedOn })
        .ToListAsync(ct);
    return Results.Ok(logs);
}).RequireAuthorization("audit.read").WithOpenApi();

app.Run();

sealed class DbMigrateWorker : BackgroundService
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
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
        _logger.LogInformation("Applying migrations for IsoAudit...");
        await db.Database.MigrateAsync(stoppingToken);
        _logger.LogInformation("Migrations applied.");
    }
}

sealed class IsoAuditConsumerWorker : BackgroundService
{
    static Guid DeterministicGuid(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        // use first 16 bytes
        return new Guid(bytes[..16]);
    }
    private readonly ILogger<IsoAuditConsumerWorker> _logger;
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;

    public IsoAuditConsumerWorker(ILogger<IsoAuditConsumerWorker> logger, IServiceProvider sp, IConfiguration cfg)
    {
        _logger = logger;
        _sp = sp;
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = _cfg.GetValue<string>("Kafka:Topics:AuditEvents") ?? "sw.iso.audit";
        var conf = new ConsumerConfig
        {
            BootstrapServers = _cfg.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092",
            GroupId = _cfg.GetValue<string>("Kafka:ConsumerGroup") ?? "iso-audit-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Subscribe(topic);
        _logger.LogInformation("IsoAudit consumer subscribed to {Topic}", topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (cr is null) continue;

                using var doc = JsonDocument.Parse(cr.Message.Value);
                var root = doc.RootElement;

                if (!root.TryGetProperty("eventName", out var en) || en.GetString() != "iso.audit.v1")
                    continue;

                var payload = root.GetProperty("payload");
                var traceId = payload.GetProperty("traceId").GetString() ?? cr.Message.Key ?? Guid.NewGuid().ToString("N");
                var direction = payload.GetProperty("direction").GetString() ?? "IN";
                var mti = payload.GetProperty("mti").GetString() ?? "0000";

                // Store raw FieldsJson as received (PCI-safe by design: tokenPan + masked only in extra)
                var fieldsJson = JsonSerializer.Serialize(payload);

                await using var scope = _sp.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IsoSwitchDbContext>();
                db.IsoMessageLogs.Add(new IsoMessageLogEntity
                {
                    Id = DeterministicGuid($"{traceId}|{direction}|{mti}"),
                    TraceId = traceId,
                    Direction = direction,
                    Mti = mti,
                    FieldsJson = fieldsJson
                });

                try
                {
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    // v55 idempotency: ignore duplicates (TraceId+Direction unique)
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IsoAudit consume/store failed");
            }
        }
    }
}

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
