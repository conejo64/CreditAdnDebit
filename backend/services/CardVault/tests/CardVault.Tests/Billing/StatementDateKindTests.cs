using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Tests.Billing;

/// <summary>
/// Gate 0 / T4b: <see cref="BillingService.GenerateStatementAsync"/> normalizes every incoming
/// <see cref="DateTime"/> to <see cref="DateTimeKind.Utc"/> before it is persisted. Npgsql rejects
/// <c>Kind=Unspecified</c> for <c>timestamp with time zone</c>; the InMemory provider used here
/// stores the value as-is, so the Kind of the persisted entity is observable.
/// </summary>
public sealed class StatementDateKindTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly BillingService _billing;

    public StatementDateKindTests()
    {
        _db = TestDbContextFactory.Create();
        var audit = new AuditService(_db);
        _billing = new BillingService(_db, new MinimumPaymentService(_db), new CreditPolicyService(_db), audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GenerateStatement_UnspecifiedKindDates_ArePersistedAsUtcCalendarDates()
    {
        var account = await SeedCreditAccountAsync();

        // A date-only JSON body ("2025-01-31") deserializes to Kind=Unspecified.
        var cycleStart = new DateTime(2025, 1, 1);
        var cycleEnd = new DateTime(2025, 1, 31, 23, 59, 59);
        var statementDate = new DateTime(2025, 1, 31);
        statementDate.Kind.Should().Be(DateTimeKind.Unspecified);

        var st = await _billing.GenerateStatementAsync(account.Id, cycleStart, cycleEnd, statementDate, dueDateOverride: null, CancellationToken.None);

        var persisted = await _db.Statements.AsNoTracking().SingleAsync(x => x.Id == st.Id);
        persisted.StatementDate.Kind.Should().Be(DateTimeKind.Utc);
        persisted.DueDate.Kind.Should().Be(DateTimeKind.Utc, "the due date is derived from the normalized statement date");
        persisted.CycleStart.Kind.Should().Be(DateTimeKind.Utc);
        persisted.CycleEnd.Kind.Should().Be(DateTimeKind.Utc);

        // Calendar-date semantics: an unspecified date is the same wall-clock date in UTC, never shifted.
        persisted.StatementDate.Should().Be(new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc));
        persisted.CycleEnd.Should().Be(new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GenerateStatement_UnspecifiedDueDateOverride_IsPersistedAsUtc()
    {
        var account = await SeedCreditAccountAsync();
        var dueDate = new DateTime(2025, 2, 20);

        var st = await _billing.GenerateStatementAsync(account.Id,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc),
            new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            dueDateOverride: dueDate, CancellationToken.None);

        var persisted = await _db.Statements.AsNoTracking().SingleAsync(x => x.Id == st.Id);
        persisted.DueDate.Kind.Should().Be(DateTimeKind.Utc);
        persisted.DueDate.Should().Be(new DateTime(2025, 2, 20, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GenerateStatement_LocalKindDate_IsConvertedToUtcInstant()
    {
        var account = await SeedCreditAccountAsync();
        var local = new DateTime(2025, 1, 31, 12, 0, 0, DateTimeKind.Local);

        var st = await _billing.GenerateStatementAsync(account.Id,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc),
            local, dueDateOverride: null, CancellationToken.None);

        var persisted = await _db.Statements.AsNoTracking().SingleAsync(x => x.Id == st.Id);
        persisted.StatementDate.Kind.Should().Be(DateTimeKind.Utc);
        persisted.StatementDate.Should().Be(local.ToUniversalTime(), "a Local value is an instant and is converted, not relabelled");
    }

    [Fact]
    public async Task GenerateStatement_DuplicateDetection_SeesUnspecifiedAndUtcAsTheSameDate()
    {
        var account = await SeedCreditAccountAsync();
        var utc = new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        await _billing.GenerateStatementAsync(account.Id, utc.AddDays(-30), utc, utc, dueDateOverride: null, CancellationToken.None);

        var act = () => _billing.GenerateStatementAsync(account.Id, utc.AddDays(-30), utc, new DateTime(2025, 1, 31), dueDateOverride: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Statement already generated for this date");
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public void UtcCalendarDate_Normalize_KeepsWallClockForUnspecifiedAndUtc(DateTimeKind kind)
    {
        var value = new DateTime(2025, 1, 31, 10, 30, 0, kind);

        var normalized = UtcCalendarDate.Normalize(value);

        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Ticks.Should().Be(value.Ticks);
    }

    [Fact]
    public void UtcCalendarDate_Normalize_ConvertsLocal()
    {
        var value = new DateTime(2025, 1, 31, 10, 30, 0, DateTimeKind.Local);

        var normalized = UtcCalendarDate.Normalize(value);

        normalized.Should().Be(value.ToUniversalTime());
        normalized.Kind.Should().Be(DateTimeKind.Utc);
    }

    private async Task<CardAccountEntity> SeedCreditAccountAsync()
    {
        var customerId = Guid.NewGuid();
        _db.Customers.Add(new CustomerEntity
        {
            Id = customerId,
            CustomerNumber = $"C{customerId:N}"[..8],
            FullName = "Statement Kind Customer",
            DocumentId = $"DOC{customerId:N}"[..10],
            Email = "kind@test.com",
            Phone = "+593999000003",
        });

        var account = _db.Accounts.Add(new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            AccountNumber = $"ACC{Guid.NewGuid():N}"[..10],
            AccountType = AccountType.Credit,
            ProductCode = "KIND_PROD",
            CreditLimit = 5000m,
            AvailableLimit = 5000m,
            Status = AccountStatus.Active,
        }).Entity;

        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Type = LedgerEntryType.Purchase,
            Amount = 200m,
            Description = "PURCHASE",
            PostedOn = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero)
        });

        await _db.SaveChangesAsync();
        return account;
    }
}
