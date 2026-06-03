using BuildingBlocks.Kafka;
using BuildingBlocks.Outbox;
using CardVault.Api.Contracts;
using CardVault.Api.Security;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Infrastructure.Persistence.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using CardVault.Api.Services;
using CardVault.Api.Services.Notifications;
using CardVault.Api.Services.Notifications.Providers;
using CardVault.Api.Services.Notifications.Templates;
using CardVault.Api.Services.Notifications.Webhooks;
using CardVault.Api.Background;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Api.Pci;
using CardVault.Api.Vault;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Switch;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));
// v26 - OpenTelemetry (Tracing + Metrics)
var otelServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "CardVault.Api";
var otelOtlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"]; // e.g., http://localhost:4317
var otelConsole = (builder.Configuration["OpenTelemetry:ConsoleExporter"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService(otelServiceName)).WithTracing(t =>
{
    t.AddSource("CardVault").AddSource("BuildingBlocks.Kafka").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (!string.IsNullOrWhiteSpace(otelOtlpEndpoint))
        t.AddOtlpExporter(o => o.Endpoint = new Uri(otelOtlpEndpoint));
    if (otelConsole)
        t.AddConsoleExporter();
}).WithMetrics(m =>
{
    m.AddMeter("CardVault.Metrics").AddAspNetCoreInstrumentation().AddRuntimeInstrumentation().AddMeter("BuildingBlocks.Kafka.Metrics").AddPrometheusExporter();
});
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<IssuerService>();
builder.Services.AddScoped<CreditPolicyService>();
builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<LoyaltyService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<CreditLimitManagementService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<BillingMaintenanceService>();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddScoped<DisputesService>();
builder.Services.AddScoped<PaymentAllocatorService>();
builder.Services.AddScoped<MinimumPaymentService>();
builder.Services.AddScoped<DailyInterestAccrualService>();
builder.Services.AddScoped<FeeService>();
builder.Services.AddScoped<DisputeService>();
builder.Services.AddScoped<HoldService>();
builder.Services.AddScoped<HoldMaintenanceService>();
builder.Services.AddScoped<AvailableCreditService>();
builder.Services.AddScoped<RiskDecisionService>();
builder.Services.AddScoped<AuthDecisionPublisher>();
builder.Services.AddScoped<StatementPdfService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<InstallmentService>();
builder.Services.AddScoped<PinService>();
builder.Services.AddScoped<ThreeDsService>();
builder.Services.AddScoped<NotificationService>();
// ── Slice 1a: Notification channel abstractions (interfaces, FSM, registry stub) ──
builder.Services.AddSingleton<IDeliveryStateMachine>(new DeliveryStateMachine());
// ── Slice 1b: Dispatcher options + provider options ──
builder.Services.Configure<NotificationDispatcherOptions>(builder.Configuration.GetSection("Notifications:Dispatcher"));
builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection("Notifications:Providers:Twilio"));
builder.Services.Configure<SendGridOptions>(builder.Configuration.GetSection("Notifications:Providers:SendGrid"));
// ── Slice 1b: SendGrid email provider (typed HttpClient) ──
builder.Services.AddSingleton<IApiKeyProvider, EnvironmentSendGridApiKeyProvider>();
builder.Services.AddHttpClient<SendGridEmailProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.sendgrid.com");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<INotificationProvider>(sp =>
    sp.GetRequiredService<SendGridEmailProvider>());
// ── Slice 2a: Movistar Ecuador SMS provider (typed HttpClient, registered BEFORE Twilio) ──
builder.Services.Configure<MovistarOptions>(builder.Configuration.GetSection("Notifications:Providers:MovistarEc"));
builder.Services.AddSingleton<IMovistarApiKeyProvider, EnvironmentMovistarApiKeyProvider>();
builder.Services.AddHttpClient<MovistarEcuadorSmsProvider>(client =>
{
    var baseUrl = builder.Configuration["Notifications:Providers:MovistarEc:BaseUrl"]
                  ?? "https://sms.movistar.ec";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<INotificationProvider>(sp =>
    sp.GetRequiredService<MovistarEcuadorSmsProvider>());
// ── Slice 1b: Twilio SMS provider (typed HttpClient) ──
builder.Services.AddSingleton<ITwilioAuthTokenProvider, EnvironmentTwilioAuthTokenProvider>();
builder.Services.AddHttpClient<TwilioSmsProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.twilio.com");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<INotificationProvider>(sp =>
    sp.GetRequiredService<TwilioSmsProvider>());
// Registry wires all registered INotificationProvider implementations (SendGrid + Twilio from 1b).
builder.Services.AddSingleton<INotificationProviderRegistry>(sp =>
    new NotificationProviderRegistry(sp.GetServices<INotificationProvider>()));
// ── Slice 1c: Razor template renderer + PCI guard ──
builder.Services.AddSingleton<PciTemplateGuard>();
builder.Services.AddScoped<INotificationTemplateRenderer>(_ => RazorNotificationTemplateRenderer.Create());
// ── Slice 1e: Webhook signature validator options + keyed registrations ──
builder.Services.Configure<TwilioWebhookOptions>(builder.Configuration.GetSection("Notifications:Providers:Twilio"));
builder.Services.Configure<SendGridWebhookOptions>(builder.Configuration.GetSection("Notifications:Webhook:SendGrid"));
builder.Services.Configure<MovistarWebhookOptions>(builder.Configuration.GetSection("Notifications:Webhook:MovistarEc"));
builder.Services.AddKeyedSingleton<IWebhookSignatureValidator>("twilio",
    (sp, _) => new TwilioWebhookSignatureValidator(sp.GetRequiredService<IOptions<TwilioWebhookOptions>>()));
builder.Services.AddKeyedSingleton<IWebhookSignatureValidator>("sendgrid",
    (sp, _) => new SendGridWebhookSignatureValidator(sp.GetRequiredService<IOptions<SendGridWebhookOptions>>()));
builder.Services.AddKeyedSingleton<IWebhookSignatureValidator>("movistar-ec",
    (sp, _) => new MovistarWebhookSignatureValidator(sp.GetRequiredService<IOptions<MovistarWebhookOptions>>()));
builder.Services.AddScoped<OpenBankingService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddSwaggerGen();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Pci").Get<PciOptions>() ?? new PciOptions());
builder.Services.AddScoped<PciAuditPublisher>();
builder.Services.AddTransient<CardVault.Api.Pci.RequestIdMiddleware>();
// DbContexts
builder.Services.AddDbContext<CardVaultDbContext>(opt => opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddDbContext<IdentityAppDbContext>(opt => opt.UseSqlServer(builder.Configuration.GetConnectionString("SqlServerIdentity")));
// Identity + MFA
builder.Services.AddIdentityCore<AppUser>(opt =>
{
    opt.Password.RequiredLength = 8;
    opt.User.RequireUniqueEmail = true;
    opt.SignIn.RequireConfirmedAccount = false;
    opt.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<IdentityAppDbContext>().AddSignInManager().AddDefaultTokenProviders();
// AuthN/AuthZ
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
    static bool HasScope(ClaimsPrincipal user, string scope)
        => user.Claims
            .Where(c => c.Type == "scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(x => string.Equals(x, scope, StringComparison.OrdinalIgnoreCase));

    // Helper: rol canónico OR permiso granular
    static bool RoleOrPerm(ClaimsPrincipal user, string perm, params string[] roles)
        => roles.Any(user.IsInRole) || user.HasClaim("perm", perm);

    options.AddPolicy("CanOperateIssuer",       p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.IssuerOperate,        "Admin", "Operator")));
    options.AddPolicy("CanManageSwitchRoutes",  p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.RoutingManage,         "Admin")));
    options.AddPolicy("CanManageCards",         p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.CardsManage,           "Admin", "Operator")));
    options.AddPolicy("CanManageCreditPolicies",p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.CreditPoliciesManage,  "Admin")));
    options.AddPolicy("CanViewLedger",          p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.LedgerView,            "Admin", "Operator", "Auditor")));
    options.AddPolicy("CanOperateLedger",       p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.LedgerOperate,         "Admin", "Operator")));
    options.AddPolicy("CanViewBilling",         p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.BillingView,           "Admin", "Operator", "Auditor")));
    options.AddPolicy("CanOperateBilling",      p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.BillingOperate,        "Admin", "Operator")));
    options.AddPolicy("CanManageBillingPolicies",p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.BillingPoliciesManage,"Admin")));
    options.AddPolicy("CanManageRisk",          p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.RiskManage,            "Admin", "Operator")));
    options.AddPolicy("CanViewDisputes",        p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.DisputesView,          "Admin", "Operator", "Auditor")));
    options.AddPolicy("CanManageDisputes",      p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.DisputesManage,        "Admin", "Operator")));
    options.AddPolicy("CanViewSettlement",      p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.SettlementView,        "Admin", "Auditor")));
    options.AddPolicy("CanRunSettlement",       p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.SettlementRun,         "Admin")));
    options.AddPolicy("CanOperateSwitch",       p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.SwitchOperate,         "Admin", "Operator")));
    options.AddPolicy("CanViewSwitchMonitor",   p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.SwitchMonitor,         "Admin", "Auditor")));
    options.AddPolicy("CanViewAudit",           p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.AuditView,             "Admin", "Auditor")));
    options.AddPolicy("CanRotateVaultKeys",     p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.VaultRotateKeys,       "Admin")));
    options.AddPolicy("CanManageUsersRoles",    p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.UsersManage,           "Admin")));
    options.AddPolicy("CanDetokenize",          p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.VaultDetokenize,       "Admin")));
    options.AddPolicy("CanViewAnalytics",       p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.AnalyticsView,         "Admin", "Operator", "Auditor")));
    options.AddPolicy("CanViewLoyalty",         p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.LoyaltyView,           "Admin", "Operator", "Auditor")));
    options.AddPolicy("CanManageLoyalty",       p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.LoyaltyManage,         "Admin", "Operator")));
    options.AddPolicy("CanManageWallets",       p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.WalletsManage,         "Admin", "Operator")));
    options.AddPolicy("CanOperateWalletPayments",p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.WalletsPay,           "Admin", "Operator")));
    options.AddPolicy("CanViewCreditLimits",    p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.CreditLimitsView,      "Admin", "Operator", "Auditor")));
    options.AddPolicy("CanManageCreditLimits",  p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.CreditLimitsManage,    "Admin", "Operator")));
    options.AddPolicy("CanViewAccounting",      p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.AccountingView,        "Admin", "Auditor")));
    options.AddPolicy("CanManageAccounting",    p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.AccountingManage,      "Admin")));
    options.AddPolicy("CanViewCollections",     p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.CollectionsView,       "Admin", "Operator", "Auditor")));
    options.AddPolicy("CanManageCollections",   p => p.RequireAssertion(ctx => RoleOrPerm(ctx.User, PermissionCatalog.CollectionsManage,     "Admin", "Operator")));
    options.AddPolicy("CanReadOpenBankingBalances", p => p.RequireAssertion(ctx =>
        string.Equals(ctx.User.FindFirstValue("grant_type"), "client_credentials", StringComparison.OrdinalIgnoreCase) &&
        HasScope(ctx.User, "ob:balances")));
    options.AddPolicy("CanReadOpenBankingTransactions", p => p.RequireAssertion(ctx =>
        string.Equals(ctx.User.FindFirstValue("grant_type"), "client_credentials", StringComparison.OrdinalIgnoreCase) &&
        HasScope(ctx.User, "ob:transactions")));
});
// Resolve vault options early so the rate limiter can bind limits from config
var vaultOptForRateLimit = builder.Configuration.GetSection("Vault").Get<CardVault.Api.Vault.VaultOptions>() ?? new CardVault.Api.Vault.VaultOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("vault_detokenize", httpContext => RateLimitPartition.GetFixedWindowLimiter(partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon", factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0 }));
    options.AddPolicy("auth_password_reset", httpContext => RateLimitPartition.GetFixedWindowLimiter(partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon", factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0 }));
    options.AddPolicy("vault_admin_ops", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = vaultOptForRateLimit.AdminRateLimit.PermitLimit,
            Window = TimeSpan.FromSeconds(vaultOptForRateLimit.AdminRateLimit.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = vaultOptForRateLimit.AdminRateLimit.QueueLimit
        }));
    // ── Slice 1e: per-provider webhook delivery-callback rate-limit ──
    // Limit is read from config at request time so tests can override via UseSetting().
    // Config key: Notifications:Webhook:RateLimits:{providerId}  (case-insensitive)
    // Partition key: webhook:{providerId}  (isolated per provider)
    var webhookRlConfig = builder.Configuration;
    options.AddPolicy("notifications_webhook", httpContext =>
    {
        var providerId = httpContext.GetRouteValue("providerId")?.ToString() ?? "unknown";
        var limit = int.TryParse(
            webhookRlConfig[$"Notifications:Webhook:RateLimits:{providerId}"],
            out var l) ? l : 100;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"webhook:{providerId}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});
// Vault (internal tokenization)
var vaultOpt = builder.Configuration.GetSection("Vault").Get<CardVault.Api.Vault.VaultOptions>() ?? new CardVault.Api.Vault.VaultOptions();
builder.Services.AddSingleton(vaultOpt);
builder.Services.AddSingleton<CardVault.Api.Vault.VaultCrypto>();
builder.Services.AddScoped<CardVault.Api.Vault.TokenVaultService>();
builder.Services.AddScoped<CardVault.Api.Vault.VaultSettingsStore>();
// Vault job options + hosted services
var vaultJobOpt = builder.Configuration.GetSection("VaultJob").Get<CardVault.Api.Vault.VaultJobOptions>() ?? new CardVault.Api.Vault.VaultJobOptions();
builder.Services.AddSingleton(vaultJobOpt);
builder.Services.AddHostedService<CardVault.Api.Vault.VaultStartupInitializer>();
builder.Services.AddHostedService<CardVault.Api.Vault.VaultReencryptHostedService>();
// Kafka event bus
var kafka = builder.Configuration.GetSection("Kafka");
builder.Services.AddSingleton<IEventBus>(_ => new KafkaEventBus(kafka["BootstrapServers"] ?? "localhost:9092", kafka["ClientId"] ?? "cardvault", kafka["SigningSecret"]));
// EF Outbox publisher
builder.Services.AddHostedService<EfOutboxPublisher>();
builder.Services.AddHostedService<SwitchTxnConsumer>();
builder.Services.AddHostedService<CardVault.Api.Background.HoldExpiryWorker>();
builder.Services.AddHostedService<CardVault.Api.Background.NotificationDispatcherWorker>();
builder.Services.AddScoped<CardVault.Api.Services.Notifications.INotificationDispatcher,
    CardVault.Api.Services.Notifications.NotificationDispatcher>();
builder.Services.AddHostedService<CardVault.Api.Background.DelinquencyEvaluationWorker>();
var app = builder.Build();

// ── Startup assertion: both vault rate-limit policies must be registered ──────
{
    var rlOptions = app.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>().Value;
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CardVault.Startup");

    // Reflect into the internal policy map to verify policy names.
    // The backing field name is "<PolicyMap>k__BackingField" in .NET 9 / ASP.NET Core 9
    // (the public PolicyMap property uses a compiler-generated backing field).
    // If the field is not found (e.g., ASP.NET Core renamed it in a future version),
    // fail loudly at startup rather than silently skipping the guard — a silent skip
    // would leave the policy-registration check permanently disabled without any warning.
    var policyMapField = typeof(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions)
        .GetField("_policyMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?? typeof(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions)
        .GetField("<PolicyMap>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    if (policyMapField == null)
        throw new InvalidOperationException(
            "Startup assertion failed: could not locate RateLimiterOptions policies via reflection. " +
            "ASP.NET Core may have renamed or removed this internal field. " +
            "Update Program.cs startup guard to use the new field name.");

    if (policyMapField.GetValue(rlOptions) is System.Collections.IDictionary policyMap)
    {
        if (!policyMap.Contains("vault_detokenize"))
            throw new InvalidOperationException(
                "Rate-limit policy 'vault_detokenize' is not registered. " +
                "CardVault cannot start without all required vault rate-limit policies.");

        if (!policyMap.Contains("vault_admin_ops"))
            throw new InvalidOperationException(
                "Rate-limit policy 'vault_admin_ops' is not registered. " +
                "CardVault cannot start without all required vault rate-limit policies.");
    }

    startupLogger.LogInformation(
        "Vault admin rate-limit bound: {PermitLimit} req / {Window}s / queue {Queue}",
        vaultOptForRateLimit.AdminRateLimit.PermitLimit,
        vaultOptForRateLimit.AdminRateLimit.WindowSeconds,
        vaultOptForRateLimit.AdminRateLimit.QueueLimit);
}

// Create DBs quickly in Development (fast test). In prod, use Migrate().
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var cardDb = sp.GetRequiredService<CardVaultDbContext>();
    var idDb = sp.GetRequiredService<IdentityAppDbContext>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var retryCount = 0;
    while (retryCount < 5)
    {
        try
        {
            logger.LogInformation("Attempting to apply migrations (Attempt {RetryCount}/5)...", retryCount + 1);
            if (app.Environment.IsDevelopment())
            {
                cardDb.Database.EnsureCreated();
                try { idDb.Database.Migrate(); } catch { idDb.Database.EnsureCreated(); }
            }
            else
            {
                cardDb.Database.Migrate();
                idDb.Database.Migrate();
            }
            logger.LogInformation("Migrations applied successfully.");
            break;
        }
        catch (Exception ex)
        {
            retryCount++;
            if (retryCount >= 5)
            {
                logger.LogCritical(ex, "Could not connect to databases after 5 attempts. Shutting down.");
                throw;
            }
            logger.LogWarning("Database not ready yet, retrying in 5 seconds... ({Message})", ex.Message);
            Thread.Sleep(5000);
        }
    }

    // Seed roles + default admin
    var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = ["Admin", "Operator", "Auditor"];
    foreach (var r in roles)
        if (!await roleMgr.RoleExistsAsync(r))
            await roleMgr.CreateAsync(new IdentityRole(r));
    var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
    var adminEmail = app.Configuration["Seed:AdminEmail"] ?? "admin@demo.com";
    var sharedPass = app.Configuration["Seed:AdminPassword"] ?? "Admin1234!";
    var seedUsers = new[]
    {
        new { Email = adminEmail, Roles = new[] { "Admin" }, Permissions = new[] { "vault:detokenize" } },
        new { Email = "operator@demo.com", Roles = new[] { "Operator" }, Permissions = Array.Empty<string>() },
        new { Email = "auditor@demo.com", Roles = new[] { "Auditor" }, Permissions = Array.Empty<string>() },
        new { Email = "admin.auditor@demo.com", Roles = new[] { "Admin", "Auditor" }, Permissions = new[] { "vault:detokenize" } },
        new { Email = "breakglass@demo.com", Roles = new[] { "Admin", "Operator", "Auditor" }, Permissions = new[] { "vault:detokenize" } }
    };

    foreach (var seedUser in seedUsers)
    {
        var user = await userMgr.FindByEmailAsync(seedUser.Email);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = seedUser.Email,
                Email = seedUser.Email,
                EmailConfirmed = true
            };

            var created = await userMgr.CreateAsync(user, sharedPass);
            if (!created.Succeeded)
                continue;
        }

        var currentRoles = await userMgr.GetRolesAsync(user);
        var rolesToAdd = seedUser.Roles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToAdd.Length > 0)
            await userMgr.AddToRolesAsync(user, rolesToAdd);

        var currentClaims = await userMgr.GetClaimsAsync(user);
        foreach (var permission in seedUser.Permissions)
        {
            if (!currentClaims.Any(c => c.Type == "perm" && string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase)))
                await userMgr.AddClaimAsync(user, new Claim("perm", permission));
        }
    }

    // Seed minimal catalogs (Development)
    if (app.Environment.IsDevelopment())
    {
        // Countries
        if (!await cardDb.Countries.AnyAsync())
        {
            cardDb.Countries.AddRange(new CountryEntity { Code = "EC", Name = "Ecuador", NumericCode = "218", Currency = "USD", Enabled = true }, new CountryEntity { Code = "US", Name = "United States", NumericCode = "840", Currency = "USD", Enabled = true });
        }

        // Card products
        if (!await cardDb.CardProducts.AnyAsync())
        {
            cardDb.CardProducts.AddRange(new CardProductEntity { Code = "VISA_CREDIT_CLASSIC", Brand = "VISA", ProductType = "CREDIT", Name = "Visa Classic", Enabled = true, UpdatedOn = DateTimeOffset.UtcNow }, new CardProductEntity { Code = "VISA_DEBIT", Brand = "VISA", ProductType = "DEBIT", Name = "Visa Debit", Enabled = true, UpdatedOn = DateTimeOffset.UtcNow });
        }

        // BIN ranges
        if (!await cardDb.BinRanges.AnyAsync())
        {
            cardDb.BinRanges.AddRange(new BinRangeEntity { BinStart = 400000, BinEnd = 499999, Brand = "VISA", Product = "CREDIT", IssuerName = "Demo Issuer", CountryCode = "EC", Enabled = true, UpdatedOn = DateTimeOffset.UtcNow });
        }

        await cardDb.SaveChangesAsync();

        if (!await cardDb.LedgerAccounts.AnyAsync())
        {
            cardDb.LedgerAccounts.AddRange(
                new CardVault.Infrastructure.Persistence.Accounting.LedgerAccountEntity { Id = Guid.NewGuid(), AccountCode = "100100", AccountName = "Cash Settlement", AccountType = "ASSET", CurrencyCode = "USD", Status = "ACTIVE" },
                new CardVault.Infrastructure.Persistence.Accounting.LedgerAccountEntity { Id = Guid.NewGuid(), AccountCode = "110100", AccountName = "Cardholder Receivable", AccountType = "ASSET", CurrencyCode = "USD", Status = "ACTIVE" },
                new CardVault.Infrastructure.Persistence.Accounting.LedgerAccountEntity { Id = Guid.NewGuid(), AccountCode = "110200", AccountName = "Settlement Receivable", AccountType = "ASSET", CurrencyCode = "USD", Status = "ACTIVE" },
                new CardVault.Infrastructure.Persistence.Accounting.LedgerAccountEntity { Id = Guid.NewGuid(), AccountCode = "210100", AccountName = "Network Clearing Payable", AccountType = "LIABILITY", CurrencyCode = "USD", Status = "ACTIVE" },
                new CardVault.Infrastructure.Persistence.Accounting.LedgerAccountEntity { Id = Guid.NewGuid(), AccountCode = "410100", AccountName = "Fee Income", AccountType = "INCOME", CurrencyCode = "USD", Status = "ACTIVE" },
                new CardVault.Infrastructure.Persistence.Accounting.LedgerAccountEntity { Id = Guid.NewGuid(), AccountCode = "410200", AccountName = "Interest Income", AccountType = "INCOME", CurrencyCode = "USD", Status = "ACTIVE" },
                new CardVault.Infrastructure.Persistence.Accounting.LedgerAccountEntity { Id = Guid.NewGuid(), AccountCode = "510100", AccountName = "Chargeback Expense", AccountType = "EXPENSE", CurrencyCode = "USD", Status = "ACTIVE" }
            );
            await cardDb.SaveChangesAsync();
        }

        if (!await cardDb.AccountingMappings.AnyAsync())
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            cardDb.AccountingMappings.AddRange(
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.PurchasePosted, ProductCode = "*", DebitAccountCode = "110100", CreditAccountCode = "210100", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.PaymentApplied, ProductCode = "*", DebitAccountCode = "100100", CreditAccountCode = "110100", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.FeePosted, ProductCode = "*", DebitAccountCode = "110100", CreditAccountCode = "410100", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.InterestPosted, ProductCode = "*", DebitAccountCode = "110100", CreditAccountCode = "410200", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.RefundPosted, ProductCode = "*", DebitAccountCode = "210100", CreditAccountCode = "110100", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.ReversalPosted, ProductCode = "*", DebitAccountCode = "210100", CreditAccountCode = "110100", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.ChargebackPosted, ProductCode = "*", DebitAccountCode = "510100", CreditAccountCode = "110100", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.ClearingPosted, ProductCode = "*", DebitAccountCode = "210100", CreditAccountCode = "110200", EffectiveDate = today },
                new CardVault.Infrastructure.Persistence.Accounting.AccountingMappingEntity { Id = Guid.NewGuid(), EventType = CardVault.Api.Services.AccountingService.SettlementBatchPosted, ProductCode = "*", DebitAccountCode = "110200", CreditAccountCode = "210100", EffectiveDate = today }
            );
            await cardDb.SaveChangesAsync();
        }

        if (!await cardDb.OpenBankingClients.AnyAsync())
        {
            var secret = app.Configuration["Seed:OpenBankingClientSecret"] ?? "OpenBanking123!";
            cardDb.OpenBankingClients.Add(new CardVault.Infrastructure.Persistence.OpenBanking.OpenBankingClientEntity
            {
                Id = Guid.NewGuid(),
                ClientId = "ob_demo_client",
                Name = "Demo Open Banking Client",
                SecretHash = CardVault.Api.Services.OpenBankingService.HashSecret(secret),
                AllowedScopes = "ob:balances ob:transactions",
                Enabled = true,
                AllowAllAccounts = true,
                CreatedOn = DateTimeOffset.UtcNow,
                UpdatedOn = DateTimeOffset.UtcNow
            });
            await cardDb.SaveChangesAsync();
        }

        if (!await cardDb.RewardPrograms.AnyAsync())
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            cardDb.RewardPrograms.AddRange(
                new CardVault.Infrastructure.Persistence.Loyalty.RewardProgramEntity
                {
                    Id = Guid.NewGuid(),
                    ProductCode = "VISA_CREDIT_CLASSIC",
                    ProgramName = "Classic Cashback Plus",
                    CashbackRate = 0.01m,
                    PointsPerCurrencyUnit = 1m,
                    CurrencyCode = "USD",
                    IsActive = true,
                    EffectiveDate = today
                },
                new CardVault.Infrastructure.Persistence.Loyalty.RewardProgramEntity
                {
                    Id = Guid.NewGuid(),
                    ProductCode = "VISA_DEBIT",
                    ProgramName = "Debit Everyday Rewards",
                    CashbackRate = 0.0025m,
                    PointsPerCurrencyUnit = 0.25m,
                    CurrencyCode = "USD",
                    IsActive = true,
                    EffectiveDate = today
                });
            await cardDb.SaveChangesAsync();
        }

        if (!await cardDb.RewardCatalogItems.AnyAsync())
        {
            cardDb.RewardCatalogItems.AddRange(
                new CardVault.Infrastructure.Persistence.Loyalty.RewardCatalogItemEntity
                {
                    Id = Guid.NewGuid(),
                    Code = "STATEMENT_CREDIT_10",
                    Name = "Statement credit 10 USD",
                    Description = "Apply a 10 USD statement credit using accumulated cashback.",
                    CashbackCost = 10m,
                    PointsCost = 0m,
                    Status = CardVault.Infrastructure.Persistence.Loyalty.RewardCatalogItemStatus.Active
                },
                new CardVault.Infrastructure.Persistence.Loyalty.RewardCatalogItemEntity
                {
                    Id = Guid.NewGuid(),
                    Code = "AIRPORT_LOUNGE_DAYPASS",
                    Name = "Airport lounge day pass",
                    Description = "Redeem points for a lounge access benefit.",
                    CashbackCost = 0m,
                    PointsCost = 2500m,
                    Status = CardVault.Infrastructure.Persistence.Loyalty.RewardCatalogItemStatus.Active
                });
            await cardDb.SaveChangesAsync();
        }
    }
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseMiddleware<RequestIdMiddleware>();
app.UseAuthorization();
app.MapPrometheusScrapingEndpoint("/metrics");
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { service = "CardVault", status = "ok" }));
app.MapGet("/health/vault", async (VaultSettingsStore store, CancellationToken ct) =>
{
    var s = await store.GetAsync(ct);
    return Results.Ok(new { activeKeyId = s.ActiveKeyId, updatedOn = s.UpdatedOn, lastReencryptRunOn = s.LastReencryptRunOn, lastReencryptUpdated = s.LastReencryptUpdated, lastReencryptStatus = s.LastReencryptStatus });
}).RequireAuthorization("CanViewAudit");
// -------------------- AUTH MOVED TO CONTROLLERS --------------------
// -------------------- ROUTING RULES MOVED TO CONTROLLERS --------------------
// -------------------- CATALOGS MOVED TO CONTROLLERS --------------------
// -------------------- DEMO PUBLISH --------------------
// -------------------- TOKENS, VAULT, AND AUDIT MOVED TO CONTROLLERS --------------------
// v28 - Audit read (PCI-safe)
// -------------------- SETTLEMENT, HOLDS, AND DISPUTES MOVED TO CONTROLLERS --------------------
app.MapControllers();
app.Run();
// Expose Program class for WebApplicationFactory in integration tests
public partial class Program { }
public sealed record CreateCustomerRequest(string FullName, string DocumentId, string Email, string Phone, string DocumentType, string Gender, string BillingAddress, string StatementAddress, string ResidenceCity, string StatementCity, string CardDeliveryCity);
public sealed record CreateAccountRequest(Guid CustomerId, AccountType AccountType, string ProductCode, decimal CreditLimit);
public sealed record IssueCardRequest(Guid AccountId, string Bin, string Pan, string ExpiryYyMm);
public sealed record BlockCardRequest(string Reason);
public sealed record CancelCardRequest(string? Reason);
public sealed record ReplaceCardRequest(string? Reason);
public sealed record DisputeTransitionRequest(string Action, string? Notes);
public sealed record MinimumPaymentPolicyUpsert(string Code, bool IsDefault, decimal FloorAmount, decimal PrincipalPercent, decimal? CeilingAmount, bool IncludeInterest, bool IncludeFees);
public sealed record ApplyPaymentRequest(decimal Amount, DateTimeOffset? PostedOn);
public sealed record PostLedgerRequest(Guid AccountId, decimal Amount, string Description, DateTimeOffset? PostedOn);
public sealed record GenerateStatementRequest(Guid AccountId, DateTime CycleStart, DateTime CycleEnd, DateTime StatementDate, DateTime? DueDate);
public sealed record VelocityRuleUpsertRequest(string ProductCode, int WindowMinutes, int MaxCount, decimal MaxAmount, string? Description);
public sealed record MccRuleUpsertRequest(string Mcc, bool IsBlocked, decimal? PerTxnLimit, string? Description);
