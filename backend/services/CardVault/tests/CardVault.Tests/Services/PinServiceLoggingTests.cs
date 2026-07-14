using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Tests.Services;

/// <summary>
/// SEC-02 (3.14): PIN material must never appear in any sink `PinService` actually writes to,
/// across set, verify-success, verify-failure, and exception/error paths.
///
/// `PinService` takes only <c>(CardVaultDbContext, AuditService)</c> and makes zero
/// <c>ILogger</c> calls, so there is no log sink to assert against — asserting against a
/// capturing logger the SUT is not wired to would pass unconditionally and prove nothing.
/// This test instead guards the sinks the code path genuinely touches:
/// (a) the persisted <see cref="AuditEventEntity.PayloadJson"/> rows written by
/// <c>AuditService.WriteAsync</c> on every set/verify/blocked/invalid path — the real audit sink;
/// (b) the exception message thrown by <c>SetPinAsync</c> on a not-found card.
/// If a future change threads an <c>ILogger</c> into <c>PinService</c>, extend this test with a
/// capturing sink that is actually wired to the SUT so it can observe (and forbid) PIN material.
/// </summary>
public sealed class PinServiceLoggingTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PinService _sut;

    public PinServiceLoggingTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);
        _sut = new PinService(_db, _audit);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<CardEntity> CreateTestCardAsync()
    {
        var customer = new CustomerEntity
        {
            Id = Guid.NewGuid(),
            CustomerNumber = $"CUS{Guid.NewGuid():N}"[..20],
            FullName = "Test Customer",
            DocumentId = Guid.NewGuid().ToString("N")[..15],
            Email = $"{Guid.NewGuid():N}@test.com",
            Phone = "0999999999"
        };
        _db.Customers.Add(customer);

        var account = new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AccountNumber = $"ACC{Guid.NewGuid():N}"[..20],
            ProductCode = "VISA_CLASSIC",
            AccountType = AccountType.Credit,
            CreditLimit = 1000m,
            AvailableLimit = 1000m
        };
        _db.Accounts.Add(account);

        var card = new CardEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Bin = "411111",
            PanToken = Guid.NewGuid().ToString("N")[..32],
            MaskedPan = "411111******1111",
            ExpiryYyMm = "2812",
            Last4 = "1111",
            Status = CardStatus.Active
        };
        _db.Cards.Add(card);

        await _db.SaveChangesAsync(CancellationToken.None);
        return card;
    }

    private static IEnumerable<string> AllForbiddenEncodingsOf(string pin)
    {
        yield return pin;
        yield return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pin));
        yield return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(pin)).ToLowerInvariant();
        yield return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(pin)).ToUpperInvariant();
    }

    [Fact]
    public async Task SetPinAsync_Then_VerifyPinAsync_SuccessAndFailure_NeverLeakPinToAuditSink()
    {
        const string pin = "7391";
        const string wrongPin = "0002";

        var card = await CreateTestCardAsync();

        await _sut.SetPinAsync(card.Id, pin, CancellationToken.None);
        await _sut.VerifyPinAsync(card.Id, pin, CancellationToken.None);       // success path
        await _sut.VerifyPinAsync(card.Id, wrongPin, CancellationToken.None);  // failure path

        // The real audit sink PinService writes to on every call — must never carry PIN material
        // (plaintext or any base64/hex encoding of it).
        var auditEvents = await _db.AuditEvents.AsNoTracking().ToListAsync(CancellationToken.None);
        auditEvents.Should().NotBeEmpty("SetPinAsync and VerifyPinAsync must write audit events");
        auditEvents.Should().OnlyContain(e =>
            !AllForbiddenEncodingsOf(pin).Any(e.PayloadJson.Contains) &&
            !AllForbiddenEncodingsOf(wrongPin).Any(e.PayloadJson.Contains));
    }

    [Fact]
    public async Task SetPinAsync_CardNotFound_ExceptionMessageNeverContainsPin()
    {
        const string pin = "8842";

        Func<Task> act = () => _sut.SetPinAsync(Guid.NewGuid(), pin, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        var message = thrown.Which.ToString();

        AllForbiddenEncodingsOf(pin).Any(message.Contains).Should().BeFalse(
            "the not-found exception path must never include the plaintext PIN or any encoding of it");
    }
}
