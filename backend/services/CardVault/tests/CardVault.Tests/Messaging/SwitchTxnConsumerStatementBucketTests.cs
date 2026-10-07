using System.Reflection;
using System.Text.Json;
using CardVault.Application.Contracts;
using CardVault.Application.Ports;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Messaging.Consumers;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CardVault.Tests.Messaging;

/// <summary>
/// Gate 0 / T2c. The consumer recalculates the OPEN statement after every switch event through
/// <c>SwitchTxnConsumer.UpdateOpenStatementAsync</c>, a private mirror of the bucket rules in
/// <see cref="BillingService.GenerateStatementAsync"/>. These tests pin the buckets after a refund,
/// a reversal and a chargeback so the two paths cannot silently diverge: credit-type entries are
/// reported with payments (negative), never as purchases.
/// </summary>
public sealed class SwitchTxnConsumerStatementBucketTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime CycleStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CycleEnd = new(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

    private readonly CardVaultDbContext _db;
    private readonly ServiceProvider _provider;
    private readonly SwitchTxnConsumer _sut;
    private readonly LedgerService _ledger;

    public SwitchTxnConsumerStatementBucketTests()
    {
        _db = TestDbContextFactory.Create();

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddScoped<AuditService>();
        services.AddScoped<AccountingService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<CreditPolicyService>();
        services.AddScoped<FeeService>();
        services.AddScoped<MinimumPaymentService>();
        services.AddScoped<BillingService>();
        services.AddScoped<LoyaltyService>();
        services.AddScoped<DisputeService>();
        services.AddScoped<NotificationService>();
        services.AddSingleton(Substitute.For<IPciAuditPublisher>());
        services.AddSingleton<IContactDataEncryptor>(TestVaultCrypto.Create());
        _provider = services.BuildServiceProvider();

        var cfg = new ConfigurationBuilder().Build();
        _sut = new SwitchTxnConsumer(NullLogger<SwitchTxnConsumer>.Instance, cfg, _provider);

        var audit = new AuditService(_db);
        _ledger = new LedgerService(_db, audit, new AccountingService(_db, audit));
    }

    public void Dispose()
    {
        _provider.Dispose();
        _db.Dispose();
    }

    [Fact(DisplayName = "Open statement after a switch refund: purchases keep the purchase, payments carry the refund")]
    public async Task Refund_lands_in_payments_bucket_of_open_statement()
    {
        var (accountId, statementId) = await SeedAccountWithOpenStatementAsync();
        await _ledger.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);

        var evt = new SwitchRefundPostedV1(accountId, 40m, "VISA", "0220", "000202", "RRN000202", Now.AddMinutes(5));
        await InvokeHandlerAsync("HandleRefundPostedAsync", evt);

        var st = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        st.Purchases.Should().Be(100m, "a refund is not a negative purchase");
        st.Payments.Should().Be(-40m, "credits are reported with payments");
        st.NewBalance.Should().Be(60m);
        st.TotalPaymentDue.Should().Be(60m);
    }

    [Fact(DisplayName = "Open statement after a switch reversal: purchases keep the purchase, payments carry the reversal")]
    public async Task Reversal_lands_in_payments_bucket_of_open_statement()
    {
        var (accountId, statementId) = await SeedAccountWithOpenStatementAsync();
        await _ledger.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);

        var evt = new SwitchPurchaseReversedV1(accountId, 100m, "VISA", "0420", "000201", "RRN000201", Now.AddMinutes(5));
        await InvokeHandlerAsync("HandlePurchaseReversedAsync", evt);

        var st = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        st.Purchases.Should().Be(100m);
        st.Payments.Should().Be(-100m);
        st.NewBalance.Should().Be(0m, "the reversal nets the purchase");
        st.TotalPaymentDue.Should().Be(0m);
    }

    [Fact(DisplayName = "Open statement after a switch chargeback: the provisional credit is reported with payments")]
    public async Task Chargeback_lands_in_payments_bucket_of_open_statement()
    {
        var (accountId, statementId) = await SeedAccountWithOpenStatementAsync();
        await _ledger.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);

        var evt = new SwitchChargebackPostedV1(accountId, 100m, "VISA", "0422", "000203", "RRN000203", "4853", Now.AddMinutes(5));
        await InvokeHandlerAsync("HandleChargebackPostedAsync", evt);

        var st = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        st.Purchases.Should().Be(100m);
        st.Payments.Should().Be(-100m);
        st.NewBalance.Should().Be(0m);
    }

    [Fact(DisplayName = "Consumer buckets match BillingService.GenerateStatementAsync for the same purchase + refund + reversal ledger")]
    public async Task Consumer_buckets_match_generate_path()
    {
        // Consumer path
        var (accountId, statementId) = await SeedAccountWithOpenStatementAsync();
        await _ledger.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        await InvokeHandlerAsync("HandleRefundPostedAsync", new SwitchRefundPostedV1(accountId, 40m, "VISA", "0220", "000204", "RRN000204", Now.AddMinutes(5)));
        await InvokeHandlerAsync("HandlePurchaseReversedAsync", new SwitchPurchaseReversedV1(accountId, 30m, "VISA", "0420", "000205", "RRN000205", Now.AddMinutes(6)));
        var consumer = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == statementId);

        // Generate path on an isolated database with the same ledger
        using var db2 = TestDbContextFactory.Create();
        var audit2 = new AuditService(db2);
        var ledger2 = new LedgerService(db2, audit2, new AccountingService(db2, audit2));
        var billing2 = new BillingService(db2, new MinimumPaymentService(db2), new CreditPolicyService(db2), audit2);
        var account2 = await SeedAccountAsync(db2);
        await ledger2.AddEntryAsync(account2, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        await ledger2.AddEntryAsync(account2, LedgerEntryType.Refund, 40m, "REFUND", Now.AddMinutes(5), CancellationToken.None);
        await ledger2.AddEntryAsync(account2, LedgerEntryType.Reversal, 30m, "REVERSAL", Now.AddMinutes(6), CancellationToken.None);
        var generated = await billing2.GenerateStatementAsync(account2, CycleStart, CycleEnd, CycleEnd.Date, dueDateOverride: null, CancellationToken.None);

        consumer.Purchases.Should().Be(generated.Purchases).And.Be(100m);
        consumer.Payments.Should().Be(generated.Payments).And.Be(-70m);
        consumer.NewBalance.Should().Be(generated.NewBalance).And.Be(30m);
        consumer.TotalPaymentDue.Should().Be(generated.TotalPaymentDue);
        consumer.MinimumPayment.Should().Be(generated.MinimumPayment);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid AccountId, Guid StatementId)> SeedAccountWithOpenStatementAsync()
    {
        var accountId = await SeedAccountAsync(_db);

        var statementId = Guid.NewGuid();
        _db.Statements.Add(new StatementEntity
        {
            Id = statementId,
            AccountId = accountId,
            CycleStart = CycleStart,
            CycleEnd = CycleEnd,
            StatementDate = CycleEnd.Date,
            DueDate = CycleEnd.Date.AddDays(15),
            Status = StatementStatus.Open,
            CreatedOn = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        return (accountId, statementId);
    }

    private static async Task<Guid> SeedAccountAsync(CardVaultDbContext db)
    {
        var customer = db.Customers.Add(new CustomerEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Switch Bucket Test Customer",
            DocumentId = $"DOC{Guid.NewGuid():N}"[..10],
            Email = "switch-buckets@test.com",
            Phone = "+593999000006",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = db.Accounts.Add(new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AccountType = AccountType.Credit,
            ProductCode = "TEST_PROD",
            CreditLimit = 1000m,
            AvailableLimit = 1000m,
            AccountNumber = $"ACC{Guid.NewGuid():N}"[..10],
            Status = AccountStatus.Active,
        }).Entity;

        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task InvokeHandlerAsync(string handlerName, object payload)
    {
        var method = typeof(SwitchTxnConsumer).GetMethod(handlerName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("SwitchTxnConsumer must expose a private {0}(JsonElement, CancellationToken) handler", handlerName);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var task = (Task)method!.Invoke(_sut, [doc.RootElement, CancellationToken.None])!;
        await task;
    }
}
