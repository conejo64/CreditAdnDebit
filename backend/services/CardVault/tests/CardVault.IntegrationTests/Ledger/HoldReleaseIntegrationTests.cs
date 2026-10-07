using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.IntegrationTests.Ledger;

/// <summary>
/// Gate 0 / T1 on real PostgreSQL: a hold whose expiry is in the future is untouched by the
/// maintenance sweep, and releasing a due hold restores available credit to the pre-authorization
/// value without posting to the balance.
/// Mirrors <c>CardVault.Tests.Services.HoldMaintenanceServiceTests</c>.
/// </summary>
public sealed class HoldReleaseIntegrationTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private HoldMaintenanceService _sut = null!;
    private AvailableCreditService _credit = null!;

    public HoldReleaseIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
    }

    protected override void OnDbReady()
    {
        _sut = new HoldMaintenanceService(Db, new AuditService(Db));
        _credit = new AvailableCreditService(Db);
    }

    [Fact(DisplayName = "An active hold with a future expiry is not expired and keeps reducing available credit")]
    public async Task Hold_with_future_expiry_is_not_expired()
    {
        var account = await SeedCreditAccountAsync(creditLimit: 1000m);
        var hold = await SeedHoldAsync(account.Id, 100m, expiresOn: Now.AddHours(1));

        var expired = await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);

        expired.Should().Be(0, "a hold whose ExpiresOn is in the future is not due");
        hold.Status.Should().Be(HoldStatus.Active);
        hold.ReleasedOn.Should().BeNull();

        var credit = await _credit.GetAsync(account.Id, CancellationToken.None);
        credit.ActiveHolds.Should().Be(100m);
        credit.AvailableCredit.Should().Be(900m, "the pending hold keeps reducing available credit");

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var persisted = await reader.AuthorizationHolds.AsNoTracking().SingleAsync(h => h.Id == hold.Id);
        persisted.Status.Should().Be(HoldStatus.Active, "the row on disk must agree with the tracked entity");
    }

    [Fact(DisplayName = "Releasing a due hold restores available credit to the pre-authorization value and leaves the posted balance unchanged")]
    public async Task Release_does_not_inflate_available_credit()
    {
        var account = await SeedCreditAccountAsync(creditLimit: 1000m);
        var before = await _credit.GetAsync(account.Id, CancellationToken.None);
        await SeedHoldAsync(account.Id, 100m, expiresOn: Now.AddMinutes(-1));

        var duringHold = await _credit.GetAsync(account.Id, CancellationToken.None);
        duringHold.AvailableCredit.Should().Be(900m);

        var expired = await _sut.ExpireDueHoldsAsync(Now, CancellationToken.None);
        expired.Should().Be(1);

        var after = await _credit.GetAsync(account.Id, CancellationToken.None);
        after.ActiveHolds.Should().Be(0m);
        after.PostedBalance.Should().Be(before.PostedBalance, "expiring a hold never posts to the balance");
        after.AvailableCredit.Should().Be(before.AvailableCredit, "available credit returns to the pre-authorization value, not above it");

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        (await reader.LedgerEntries.AsNoTracking()
                .CountAsync(x => x.AccountId == account.Id && x.Type == LedgerEntryType.Reversal))
            .Should().Be(0, "a hold expiry is not a posted transaction");
        (await reader.LedgerEntries.AsNoTracking()
                .Where(x => x.AccountId == account.Id && x.Type == LedgerEntryType.AuthorizationHold)
                .SumAsync(x => x.Amount))
            .Should().Be(0m, "the release shadow entry nets the original hold entry to zero");
    }

    private async Task<AuthorizationHoldEntity> SeedHoldAsync(Guid accountId, decimal amount, DateTimeOffset expiresOn, string stan = "000001")
    {
        var holdLedgerId = Guid.NewGuid();
        Db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = holdLedgerId,
            AccountId = accountId,
            Type = LedgerEntryType.AuthorizationHold,
            Amount = amount,
            Description = $"AUTH HOLD VISA STAN:{stan}",
            PostedOn = expiresOn.AddHours(-72),
            StatementId = null
        });

        var hold = Db.AuthorizationHolds.Add(new AuthorizationHoldEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Network = "VISA",
            Stan = stan,
            Rrn = $"RRN{stan}",
            Amount = amount,
            CapturedAmount = 0m,
            Status = HoldStatus.Active,
            AuthorizedOn = expiresOn.AddHours(-72),
            ExpiresOn = expiresOn,
            HoldLedgerEntryId = holdLedgerId
        }).Entity;

        await Db.SaveChangesAsync();
        return hold;
    }
}
