using CardVault.Api.Services;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CardVault.Tests.Services;

/// <summary>
/// Unit tests for HoldService covering Authorize, Capture (partial + full), and Release flows.
/// HoldService uses IServiceProvider internally for RiskDecisionService and AuthDecisionPublisher.
/// We wire a real ServiceProvider with mocked Kafka bus to isolate the domain behavior.
/// </summary>
public sealed class HoldServiceTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly HoldService _sut;
    private readonly ServiceProvider _sp;

    public HoldServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);

        // Build a minimal DI container wiring the real services and a no-op event bus
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton(_audit);

        // Mock IEventBus so Kafka is never hit in tests
        var busMock = Substitute.For<BuildingBlocks.Outbox.IEventBus>();
        busMock.PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        services.AddSingleton(busMock);

        // IConfiguration with minimal Kafka key
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:SwitchResponseTopic"] = "test" })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        services.AddSingleton<AvailableCreditService>();
        services.AddSingleton<PinService>();
        services.AddSingleton<RiskDecisionService>();
        services.AddSingleton<AuthDecisionPublisher>();
        services.AddSingleton<CreditLimitManagementService>();
        services.AddSingleton(provider => new HoldService(
            provider.GetRequiredService<CardVaultDbContext>(),
            provider.GetRequiredService<AuditService>(),
            provider));

        _sp = services.BuildServiceProvider();
        _sut = _sp.GetRequiredService<HoldService>();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _db.Dispose();
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private async Task<(CardAccountEntity Account, CardEntity Card)> SeedAccountAsync(
        decimal creditLimit = 5000m, string productCode = "TEST_PROD")
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Hold Test Customer",
            DocumentId = $"DOC{Guid.NewGuid():N}"[..10],
            Email = "hold@test.com",
            Phone = "+593999000002",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = _db.Accounts.Add(new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AccountType = AccountType.Credit,
            ProductCode = productCode,
            CreditLimit = creditLimit,
            AvailableLimit = creditLimit,
            AccountNumber = $"ACC{Guid.NewGuid():N}"[..10],
            Status = AccountStatus.Active,
        }).Entity;

        var card = _db.Cards.Add(new CardEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Bin = "411111",
            PanToken = $"tok_{Guid.NewGuid():N}",
            MaskedPan = "411111******1111",
            Last4 = "1111",
            ExpiryYyMm = "2812",
            Status = CardStatus.Active,
        }).Entity;

        _db.CreditPolicies.Add(new CreditPolicyEntity
        {
            ProductCode = productCode,
            AllowOverlimit = false,
            FloorLimit = 0m,
            HoldTtlHours = 72,
        });

        await _db.SaveChangesAsync();
        return (account, card);
    }

    private Task<AuthorizationHoldEntity> AuthorizeAsync(Guid accountId,
        string stan = "000001", string rrn = "RRN000001", decimal amount = 100m)
        => _sut.AuthorizeAsync(accountId, null, "VISA", "0100", stan, rrn, null, "MERCHANT01",
            "5411", null, null, amount, DateTimeOffset.UtcNow, CancellationToken.None);

    // ─────────────────────────────────────────────────────────
    // AuthorizeAsync
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_ValidRequest_ShouldCreateHoldAndLedgerEntry()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);

        var hold = await AuthorizeAsync(account.Id);

        hold.Should().NotBeNull();
        hold.Status.Should().Be(HoldStatus.Active);
        hold.Amount.Should().Be(100m);
        hold.AccountId.Should().Be(account.Id);

        var ledger = _db.LedgerEntries.FirstOrDefault(x => x.Id == hold.HoldLedgerEntryId);
        ledger.Should().NotBeNull("a hold ledger entry must be created for each authorization");
        ledger!.Type.Should().Be(LedgerEntryType.AuthorizationHold);
        ledger.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task AuthorizeAsync_SameStan_ShouldBeIdempotent()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);

        var first = await AuthorizeAsync(account.Id, stan: "IDEMP01", rrn: "RRNIDEMP01");
        var second = await AuthorizeAsync(account.Id, stan: "IDEMP01", rrn: "RRNIDEMP01");

        second.Id.Should().Be(first.Id, "duplicate authorize must return the same hold");
        _db.AuthorizationHolds.Count(x => x.AccountId == account.Id).Should().Be(1);
    }

    [Fact]
    public async Task AuthorizeAsync_InsufficientCredit_ShouldThrow()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 50m);

        var act = () => AuthorizeAsync(account.Id, amount: 200m);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AUTH_DECLINED*");
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldWriteAuditRecord()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);

        await AuthorizeAsync(account.Id);

        var audits = await _audit.LatestAsync(10, CancellationToken.None);
        audits.Should().Contain(a => a.EventType == "holds.auth.approved");
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldReduceAvailableCredit()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);

        await AuthorizeAsync(account.Id, amount: 300m);

        var available = await _sp.GetRequiredService<AvailableCreditService>()
            .GetAsync(account.Id, CancellationToken.None);
        available.AvailableCredit.Should().Be(700m, "available credit = limit - active holds");
    }

    // ─────────────────────────────────────────────────────────
    // CaptureAsync
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAsync_FullCapture_ShouldMarkHoldCaptured()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);
        await AuthorizeAsync(account.Id, stan: "S100", rrn: "R100", amount: 200m);

        var captured = await _sut.CaptureAsync(account.Id, "VISA", "0200", "S100", "R100",
            null, 200m, DateTimeOffset.UtcNow, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Status.Should().Be(HoldStatus.Captured);
        captured.CapturedAmount.Should().Be(200m);
    }

    [Fact]
    public async Task CaptureAsync_PartialCapture_ShouldMarkHoldPartiallyCaptured()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);
        await AuthorizeAsync(account.Id, stan: "S200", rrn: "R200", amount: 300m);

        var captured = await _sut.CaptureAsync(account.Id, "VISA", "0200", "S200", "R200",
            null, 150m, DateTimeOffset.UtcNow, CancellationToken.None);

        captured!.Status.Should().Be(HoldStatus.PartiallyCaptured);
        captured.CapturedAmount.Should().Be(150m);
    }

    [Fact]
    public async Task CaptureAsync_FullThenPartial_ShouldFinalizeToCaptured()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);
        await AuthorizeAsync(account.Id, stan: "S300", rrn: "R300", amount: 200m);

        await _sut.CaptureAsync(account.Id, "VISA", "0200", "S300", "R300",
            null, 100m, DateTimeOffset.UtcNow, CancellationToken.None);

        var final = await _sut.CaptureAsync(account.Id, "VISA", "0200", "S300", "R300",
            null, 100m, DateTimeOffset.UtcNow, CancellationToken.None);

        final!.Status.Should().Be(HoldStatus.Captured);
        final.CapturedAmount.Should().Be(200m);
    }

    [Fact]
    public async Task CaptureAsync_NoMatchingHold_ShouldReturnNull()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);

        var result = await _sut.CaptureAsync(account.Id, "VISA", "0200", "NOSUCH", "NOSUCH",
            null, 100m, DateTimeOffset.UtcNow, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAsync_ShouldPostClearingLedgerEntry()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);
        await AuthorizeAsync(account.Id, stan: "S400", rrn: "R400", amount: 120m);
        var ledgerCountBefore = _db.LedgerEntries.Count(x => x.AccountId == account.Id);

        await _sut.CaptureAsync(account.Id, "VISA", "0200", "S400", "R400",
            null, 120m, DateTimeOffset.UtcNow, CancellationToken.None);

        var ledgerCountAfter = _db.LedgerEntries.Count(x => x.AccountId == account.Id);
        ledgerCountAfter.Should().Be(ledgerCountBefore + 1, "one Clearing ledger entry must be added on capture");

        var clearingEntry = _db.LedgerEntries
            .Where(x => x.AccountId == account.Id && x.Type == LedgerEntryType.Clearing)
            .OrderByDescending(x => x.PostedOn)
            .FirstOrDefault();
        clearingEntry.Should().NotBeNull();
        clearingEntry!.Amount.Should().Be(120m);
    }

    // ─────────────────────────────────────────────────────────
    // ReleaseAsync
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseAsync_ActiveHold_ShouldMarkReleased()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);
        await AuthorizeAsync(account.Id, stan: "S500", rrn: "R500", amount: 250m);

        var released = await _sut.ReleaseAsync(account.Id, "VISA", "0400", "S500", "R500",
            null, DateTimeOffset.UtcNow, CancellationToken.None);

        released.Should().NotBeNull();
        released!.Status.Should().Be(HoldStatus.Released);
    }

    [Fact]
    public async Task ReleaseAsync_ShouldPostReversalLedgerEntry()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);
        await AuthorizeAsync(account.Id, stan: "S600", rrn: "R600", amount: 100m);

        await _sut.ReleaseAsync(account.Id, "VISA", "0400", "S600", "R600",
            null, DateTimeOffset.UtcNow, CancellationToken.None);

        var reversal = _db.LedgerEntries
            .FirstOrDefault(x => x.AccountId == account.Id && x.Type == LedgerEntryType.Reversal);
        reversal.Should().NotBeNull();
        reversal!.Amount.Should().Be(-100m, "reversal amount must be negative");
    }

    [Fact]
    public async Task ReleaseAsync_ShouldHaveNoActiveHoldsAfterRelease()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);

        await AuthorizeAsync(account.Id, stan: "S700", rrn: "R700", amount: 400m);
        await _sut.ReleaseAsync(account.Id, "VISA", "0400", "S700", "R700",
            null, DateTimeOffset.UtcNow, CancellationToken.None);

        var available = await _sp.GetRequiredService<AvailableCreditService>()
            .GetAsync(account.Id, CancellationToken.None);

        // The core behavior: releasing a hold must zero out the active hold pool
        available.ActiveHolds.Should().Be(0m, "released hold must not count as active");
    }

    [Fact]
    public async Task ReleaseAsync_NoMatchingHold_ShouldReturnNull()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);

        var result = await _sut.ReleaseAsync(account.Id, "VISA", "0400", "NOSUCH", "NOSUCH",
            null, DateTimeOffset.UtcNow, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseAsync_AlreadyReleasedHold_ShouldReturnWithoutChanges()
    {
        var (account, _) = await SeedAccountAsync(creditLimit: 1000m);
        await AuthorizeAsync(account.Id, stan: "S800", rrn: "R800", amount: 100m);

        // First release
        await _sut.ReleaseAsync(account.Id, "VISA", "0400", "S800", "R800",
            null, DateTimeOffset.UtcNow, CancellationToken.None);

        // Second release of same hold — should return hold unchanged (already Released)
        var second = await _sut.ReleaseAsync(account.Id, "VISA", "0400", "S800", "R800",
            null, DateTimeOffset.UtcNow, CancellationToken.None);

        second.Should().NotBeNull();
        second!.Status.Should().Be(HoldStatus.Released, "a released hold must not change state");

        // No additional reversal ledger entry should have been posted
        var reversals = _db.LedgerEntries
            .Where(x => x.AccountId == account.Id && x.Type == LedgerEntryType.Reversal)
            .ToList();
        reversals.Should().HaveCount(1, "only one reversal entry should exist");
    }
}
