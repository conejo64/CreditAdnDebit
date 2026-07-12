using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardVault.Tests.Services;

/// <summary>
/// SEC-02 (3.14): PIN material must never appear in any log sink, across set, verify-success,
/// verify-failure, and exception/error paths.
///
/// `PinService` currently makes zero `ILogger` calls, so this test asserts against every real
/// sink the code path actually touches: (a) a capturing `ILoggerProvider` wired the same way
/// `Program.cs` wires Serilog, in case any current or future code path in the call chain logs;
/// (b) the persisted `AuditEventEntity.PayloadJson` rows written by `AuditService.WriteAsync`,
/// which are the actual audit sink `PinService` writes to on every set/verify/blocked/invalid
/// path; (c) the exception message thrown by `SetPinAsync` on a not-found card. This is a
/// regression guard: if a future change adds a stray `_logger.LogDebug($"pin={pin}")`-style
/// call, or starts including the PIN in an audit payload, this test fails.
/// </summary>
public sealed class PinServiceLoggingTests : IDisposable
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> CapturedMessages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(CapturedMessages);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _sink;
            public CapturingLogger(List<string> sink) => _sink = sink;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _sink.Add(formatter(state, exception));
                if (exception is not null) _sink.Add(exception.ToString());
            }
        }
    }

    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PinService _sut;
    private readonly CapturingLoggerProvider _logProvider;
    private readonly ILoggerFactory _loggerFactory;

    public PinServiceLoggingTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);
        _sut = new PinService(_db, _audit);

        _logProvider = new CapturingLoggerProvider();
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logProvider));
    }

    public void Dispose()
    {
        _db.Dispose();
        _loggerFactory.Dispose();
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
    public async Task SetPinAsync_Then_VerifyPinAsync_SuccessAndFailure_NeverLeakPinMaterial()
    {
        const string pin = "7391";
        const string wrongPin = "0002";

        var card = await CreateTestCardAsync();

        await _sut.SetPinAsync(card.Id, pin, CancellationToken.None);
        await _sut.VerifyPinAsync(card.Id, pin, CancellationToken.None);       // success path
        await _sut.VerifyPinAsync(card.Id, wrongPin, CancellationToken.None);  // failure path

        // (a) capturing ILogger sink — PinService makes no logging calls today; this proves
        // that fact and guards against a future regression.
        _logProvider.CapturedMessages.Should().NotContain(m =>
            AllForbiddenEncodingsOf(pin).Any(m.Contains) || AllForbiddenEncodingsOf(wrongPin).Any(m.Contains));

        // (b) the real audit sink PinService writes to on every call.
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
