using System.Reflection;
using System.Text.Json;
using CardVault.Application.Contracts;
using CardVault.Application.Ports;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Messaging.Consumers;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CardVault.Tests.Messaging;

/// <summary>
/// Behaviour tests for the <see cref="SwitchTxnConsumer"/> reversal and refund paths.
/// A reversal or refund coming from the switch must land in the ledger as a credit
/// (negative amount) and reduce the posted balance of the account.
/// The event handlers are private; they are invoked through reflection exactly as the
/// existing characterization tests do for <c>UpdateOpenStatementAsync</c>.
/// </summary>
public sealed class SwitchTxnConsumerLedgerSignTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly CardVaultDbContext _db;
    private readonly ServiceProvider _provider;
    private readonly SwitchTxnConsumer _sut;
    private readonly LedgerService _ledger;

    public SwitchTxnConsumerLedgerSignTests()
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

    private async Task<Guid> SeedAccountAsync()
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Switch Test Customer",
            DocumentId = $"DOC{Guid.NewGuid():N}"[..10],
            Email = "switch@test.com",
            Phone = "+593999000005",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = _db.Accounts.Add(new CardAccountEntity
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

        await _db.SaveChangesAsync();
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

    [Fact(DisplayName = "Switch reversal posts a negative Reversal entry and nets the purchase to zero")]
    public async Task Purchase_reversed_posts_negative_reversal()
    {
        var accountId = await SeedAccountAsync();
        await _ledger.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);

        var evt = new SwitchPurchaseReversedV1(accountId, 100m, "VISA", "0420", "000101", "RRN000101", Now.AddMinutes(5));
        await InvokeHandlerAsync("HandlePurchaseReversedAsync", evt);

        var reversal = _db.LedgerEntries.Single(x => x.AccountId == accountId && x.Type == LedgerEntryType.Reversal);
        reversal.Amount.Should().Be(-100m, "a switch reversal credits the cardholder");
        (await _ledger.GetBalanceAsync(accountId, CancellationToken.None)).Should().Be(0m);

        var journal = _db.TxnJournal.Single(x => x.Stan == "000101" && x.Rrn == "RRN000101");
        journal.Status.Should().Be("posted");
        journal.LedgerEntryId.Should().Be(reversal.Id);
    }

    [Fact(DisplayName = "Switch refund posts a negative Refund entry and reduces the posted balance")]
    public async Task Refund_posted_posts_negative_refund()
    {
        var accountId = await SeedAccountAsync();
        await _ledger.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);

        var evt = new SwitchRefundPostedV1(accountId, 40m, "VISA", "0220", "000102", "RRN000102", Now.AddMinutes(5));
        await InvokeHandlerAsync("HandleRefundPostedAsync", evt);

        var refund = _db.LedgerEntries.Single(x => x.AccountId == accountId && x.Type == LedgerEntryType.Refund);
        refund.Amount.Should().Be(-40m, "a switch refund credits the cardholder");
        (await _ledger.GetBalanceAsync(accountId, CancellationToken.None)).Should().Be(60m);
    }

    [Fact(DisplayName = "Switch chargeback posts a negative provisional credit and nets the purchase to zero")]
    public async Task Chargeback_posted_posts_negative_chargeback()
    {
        var accountId = await SeedAccountAsync();
        await _ledger.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);

        var evt = new SwitchChargebackPostedV1(accountId, 100m, "VISA", "0422", "000103", "RRN000103", "4853", Now.AddMinutes(5));
        await InvokeHandlerAsync("HandleChargebackPostedAsync", evt);

        var chargeback = _db.LedgerEntries.Single(x => x.AccountId == accountId && x.Type == LedgerEntryType.Chargeback);
        chargeback.Amount.Should().Be(-100m);
        (await _ledger.GetBalanceAsync(accountId, CancellationToken.None)).Should().Be(0m);
    }
}
