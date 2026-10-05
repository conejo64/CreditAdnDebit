using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

/// <summary>
/// Behaviour tests for <see cref="HoldMaintenanceService.ExpireDueHoldsAsync"/>.
/// Expiry must only touch holds whose ExpiresOn has passed, and releasing the
/// pending amount must never change the posted balance nor inflate available credit.
/// </summary>
public sealed class HoldMaintenanceServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly CardVaultDbContext _db;
    private readonly HoldMaintenanceService _sut;
    private readonly AvailableCreditService _credit;

    public HoldMaintenanceServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _sut = new HoldMaintenanceService(_db, new AuditService(_db));
        _credit = new AvailableCreditService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<CardAccountEntity> SeedAccountAsync(decimal creditLimit = 1000m)
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Expiry Test Customer",
            DocumentId = $"DOC{Guid.NewGuid():N}"[..10],
            Email = "expiry@test.com",
            Phone = "+593999000003",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = _db.Accounts.Add(new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AccountType = AccountType.Credit,
            ProductCode = "TEST_PROD",
            CreditLimit = creditLimit,
            AvailableLimit = creditLimit,
            AccountNumber = $"ACC{Guid.NewGuid():N}"[..10],
            Status = AccountStatus.Active,
        }).Entity;

        await _db.SaveChangesAsync();
        return account;
    }

    private async Task<AuthorizationHoldEntity> SeedHoldAsync(Guid accountId, decimal amount, DateTimeOffset expiresOn,
        HoldStatus status = HoldStatus.Active, decimal capturedAmount = 0m, string stan = "000001")
    {
        var holdLedgerId = Guid.NewGuid();
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = holdLedgerId,
            AccountId = accountId,
            Type = LedgerEntryType.AuthorizationHold,
            Amount = amount,
            Description = $"AUTH HOLD VISA STAN:{stan}",
            PostedOn = expiresOn.AddHours(-72),
            StatementId = null
        });

        if (capturedAmount > 0m)
        {
            _db.LedgerEntries.Add(new LedgerEntryEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Type = LedgerEntryType.Clearing,
                Amount = capturedAmount,
                Description = $"CLEARING VISA STAN:{stan}",
                PostedOn = expiresOn.AddHours(-48),
                StatementId = null
            });
        }

        var hold = _db.AuthorizationHolds.Add(new AuthorizationHoldEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Network = "VISA",
            Stan = stan,
            Rrn = $"RRN{stan}",
            Amount = amount,
            CapturedAmount = capturedAmount,
            Status = status,
            AuthorizedOn = expiresOn.AddHours(-72),
            ExpiresOn = expiresOn,
            HoldLedgerEntryId = holdLedgerId
        }).Entity;

        await _db.SaveChangesAsync();
        return hold;
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_ActiveHoldNotYetDue_ShouldBeUntouched()
    {
        var account = await SeedAccountAsync();
        var hold = await SeedHoldAsync(account.Id, 100m, expiresOn: Now.AddHours(1));

        var expired = await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        expired.Should().Be(0, "a hold whose ExpiresOn is in the future is not due");
        hold.Status.Should().Be(HoldStatus.Active);
        hold.ReleasedOn.Should().BeNull();

        var credit = await _credit.GetAsync(account.Id, CancellationToken.None);
        credit.ActiveHolds.Should().Be(100m);
        credit.AvailableCredit.Should().Be(900m, "the pending hold keeps reducing available credit");
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_ActiveHoldDue_ShouldExpireIt()
    {
        var account = await SeedAccountAsync();
        var hold = await SeedHoldAsync(account.Id, 100m, expiresOn: Now.AddMinutes(-1));

        var expired = await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        expired.Should().Be(1);
        hold.Status.Should().Be(HoldStatus.Expired);
        hold.ReleasedOn.Should().Be(Now);
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_HoldExpiringExactlyNow_ShouldExpireIt()
    {
        var account = await SeedAccountAsync();
        var hold = await SeedHoldAsync(account.Id, 100m, expiresOn: Now);

        var expired = await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        expired.Should().Be(1, "ExpiresOn <= now is due");
        hold.Status.Should().Be(HoldStatus.Expired);
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_PartiallyCapturedHold_ShouldFollowExpiresOn()
    {
        var account = await SeedAccountAsync(creditLimit: 2000m);
        var due = await SeedHoldAsync(account.Id, 300m, expiresOn: Now.AddMinutes(-5),
            status: HoldStatus.PartiallyCaptured, capturedAmount: 120m, stan: "000010");
        var notDue = await SeedHoldAsync(account.Id, 200m, expiresOn: Now.AddHours(2),
            status: HoldStatus.PartiallyCaptured, capturedAmount: 50m, stan: "000011");

        var expired = await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        expired.Should().Be(1);
        due.Status.Should().Be(HoldStatus.Expired);
        notDue.Status.Should().Be(HoldStatus.PartiallyCaptured);
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_ShouldRestoreAvailableCreditWithoutChangingPostedBalance()
    {
        var account = await SeedAccountAsync(creditLimit: 1000m);
        var before = await _credit.GetAsync(account.Id, CancellationToken.None);
        await SeedHoldAsync(account.Id, 100m, expiresOn: Now.AddMinutes(-1));

        var duringHold = await _credit.GetAsync(account.Id, CancellationToken.None);
        duringHold.AvailableCredit.Should().Be(900m);

        await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        var after = await _credit.GetAsync(account.Id, CancellationToken.None);
        after.ActiveHolds.Should().Be(0m);
        after.PostedBalance.Should().Be(before.PostedBalance, "expiring a hold never posts to the balance");
        after.AvailableCredit.Should().Be(before.AvailableCredit, "available credit returns to the pre-authorization value, not above it");
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_ShouldNotPostReversalLedgerEntry()
    {
        var account = await SeedAccountAsync();
        await SeedHoldAsync(account.Id, 100m, expiresOn: Now.AddMinutes(-1));

        await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        _db.LedgerEntries.Count(x => x.AccountId == account.Id && x.Type == LedgerEntryType.Reversal)
            .Should().Be(0, "a hold expiry is not a posted transaction");
        _db.LedgerEntries
            .Where(x => x.AccountId == account.Id && x.Type == LedgerEntryType.AuthorizationHold)
            .Sum(x => x.Amount)
            .Should().Be(0m, "the release shadow entry nets the original hold entry to zero");
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_PartiallyCapturedDueHold_ShouldKeepOnlyCapturedAmountPosted()
    {
        var account = await SeedAccountAsync(creditLimit: 1000m);
        await SeedHoldAsync(account.Id, 300m, expiresOn: Now.AddMinutes(-1),
            status: HoldStatus.PartiallyCaptured, capturedAmount: 120m);

        await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        var credit = await _credit.GetAsync(account.Id, CancellationToken.None);
        credit.PostedBalance.Should().Be(120m);
        credit.ActiveHolds.Should().Be(0m);
        credit.AvailableCredit.Should().Be(880m);
    }
}
