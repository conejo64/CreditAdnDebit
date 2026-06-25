using CardVault.Application.Features.Settlement.Commands;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Accounting;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Settlement;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CardVault.Tests.Handlers;

public sealed class SettlementHandlerTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly SettlementService _settlementService;
    private readonly AuditService _auditService;
    private readonly AccountingService _accountingService;

    public SettlementHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _auditService = new AuditService(_db);
        _accountingService = new AccountingService(_db, _auditService);
        _settlementService = new SettlementService(_db, _auditService, _accountingService);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAccountingAsync()
    {
        var debitAcc = new LedgerAccountEntity
        {
            Id = Guid.NewGuid(),
            AccountCode = "110200",
            AccountName = "Settlement Receivable",
            AccountType = "ASSET",
            CurrencyCode = "USD",
            Status = "ACTIVE"
        };
        var creditAcc = new LedgerAccountEntity
        {
            Id = Guid.NewGuid(),
            AccountCode = "210100",
            AccountName = "Network Clearing Payable",
            AccountType = "LIABILITY",
            CurrencyCode = "USD",
            Status = "ACTIVE"
        };
        _db.LedgerAccounts.AddRange(debitAcc, creditAcc);

        var mapping = new AccountingMappingEntity
        {
            Id = Guid.NewGuid(),
            EventType = AccountingService.SettlementBatchPosted,
            ProductCode = "*",
            DebitAccountCode = "110200",
            CreditAccountCode = "210100",
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10))
        };
        _db.AccountingMappings.Add(mapping);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task RunSettlementCommand_ShouldAggregatePurchasesAndCreateBatch()
    {
        // Arrange
        await SeedAccountingAsync();
        var accountId = Guid.NewGuid();
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayPosted = businessDate.ToDateTime(new TimeOnly(12, 0));

        var entry = new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Purchase,
            Amount = 100.50m,
            Description = "SWITCH PURCHASE VISA RRN:TEST123456",
            PostedOn = dayPosted
        };
        _db.LedgerEntries.Add(entry);
        
        // Add another one for different date (should be ignored)
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Purchase,
            Amount = 50.00m,
            Description = "SWITCH PURCHASE VISA ",
            PostedOn = dayPosted.AddDays(-1)
        });

        await _db.SaveChangesAsync();

        var handler = new RunSettlementCommandHandler(_settlementService);

        // Act
        var result = await handler.Handle(new RunSettlementCommand("Visa", businessDate), CancellationToken.None);

        // Assert
        result.Should().BeOfType<Ok<SettlementBatchEntity>>();
        var okResult = result as Ok<SettlementBatchEntity>;
        okResult!.Value!.TxnCount.Should().Be(1);
        okResult.Value.GrossAmount.Should().Be(100.50m);
        okResult.Value.Items.Should().HaveCount(1);
        okResult.Value.Items[0].LedgerEntryId.Should().Be(entry.Id);
        
        // Verify journal entry was created by AccountingService
        var journal = _db.JournalEntries.FirstOrDefault(j => j.SourceModule == "SETTLEMENT" && j.SourceReference == okResult.Value.Id.ToString("N"));
        journal.Should().NotBeNull();
        journal!.EventType.Should().Be(AccountingService.SettlementBatchPosted);

        // Verify audit log
        var audit = _db.AuditEvents.FirstOrDefault(e => e.EventType == "settlement.v1.batch.created");
        audit.Should().NotBeNull();
        audit!.PayloadJson.Should().Contain(okResult.Value.Id.ToString());
    }
}
