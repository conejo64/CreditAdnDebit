using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Switch;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

/// <summary>
/// Unit tests for RiskDecisionService.
/// Each test covers a distinct branch of the authorization risk engine.
/// </summary>
public sealed class RiskDecisionServiceTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PinService _pin;
    private readonly AvailableCreditService _available;
    private readonly RiskDecisionService _sut;

    public RiskDecisionServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);
        _pin = new PinService(_db, _audit);
        _available = new AvailableCreditService(_db);
        _sut = new RiskDecisionService(_db, _available, _pin);
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private async Task<(CardAccountEntity Account, CardEntity Card)> CreateCreditAccountWithCardAsync(
        decimal creditLimit = 5000m, string productCode = "VISA_TEST",
        CardStatus cardStatus = CardStatus.Active, string expiryYyMm = "2812",
        AccountStatus accountStatus = AccountStatus.Active)
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Risk Test Customer",
            DocumentId = $"DOC{Guid.NewGuid():N}"[..10],
            Email = "risk@test.com",
            Phone = "+593999000001",
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
            Status = accountStatus,
        }).Entity;

        var card = _db.Cards.Add(new CardEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Bin = "411111",
            PanToken = $"tok_{Guid.NewGuid():N}",
            MaskedPan = "411111******1111",
            Last4 = "1111",
            ExpiryYyMm = expiryYyMm,
            Status = cardStatus,
        }).Entity;

        await _db.SaveChangesAsync();
        return (account, card);
    }

    private async Task SetPinAsync(Guid cardId, string pin)
        => await _pin.SetPinAsync(cardId, pin, CancellationToken.None);

    private async Task AddCreditPolicyAsync(string productCode,
        bool allowOverlimit = false, decimal overlimitBuffer = 0m,
        decimal floorLimit = 0m, int holdTtlHours = 72)
    {
        _db.CreditPolicies.Add(new CreditPolicyEntity
        {
            ProductCode = productCode,
            AllowOverlimit = allowOverlimit,
            OverlimitBufferAmount = overlimitBuffer,
            FloorLimit = floorLimit,
            HoldTtlHours = holdTtlHours,
        });
        await _db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────
    // 0. Card and account state (Gate 0 / T5)
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CardStatus.Created, "CARD_NOT_ACTIVE")]
    [InlineData(CardStatus.Personalized, "CARD_NOT_ACTIVE")]
    [InlineData(CardStatus.Printed, "CARD_NOT_ACTIVE")]
    [InlineData(CardStatus.Delivered, "CARD_NOT_ACTIVE")]
    [InlineData(CardStatus.Blocked, "CARD_BLOCKED")]
    [InlineData(CardStatus.Cancelled, "CARD_CANCELLED")]
    [InlineData(CardStatus.Expired, "CARD_EXPIRED")]
    public async Task DecideAuth_CardNotActive_ShouldDeclineWithStatusReason(CardStatus status, string expectedReason)
    {
        var (account, card) = await CreateCreditAccountWithCardAsync(cardStatus: status);

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse($"a {status} card must never be authorized");
        result.Reason.Should().Be(expectedReason);
    }

    [Theory]
    [InlineData("2001")]   // January 2020, YYMM
    [InlineData("202001")] // January 2020, YYYYMM
    public async Task DecideAuth_ActiveCardPastExpiry_ShouldDeclineWithCardExpired(string expiry)
    {
        var (account, card) = await CreateCreditAccountWithCardAsync(expiryYyMm: expiry);

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse("the expiry date on the card has passed even though its status is still Active");
        result.Reason.Should().Be("CARD_EXPIRED");
    }

    [Theory]
    [InlineData("9912")]
    [InlineData("209912")]
    public async Task DecideAuth_ActiveCardWithFutureExpiry_ShouldApprove(string expiry)
    {
        var (account, card) = await CreateCreditAccountWithCardAsync(expiryYyMm: expiry);

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.Reason.Should().Be("OK");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCD")]
    [InlineData("2813")] // month 13
    [InlineData("28")]
    public async Task DecideAuth_UnparsableExpiry_ShouldFailClosed(string expiry)
    {
        var (account, card) = await CreateCreditAccountWithCardAsync(expiryYyMm: expiry);

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse("a card whose expiry cannot be read must not be trusted");
        result.Reason.Should().Be("CARD_EXPIRY_INVALID");
    }

    [Fact]
    public async Task DecideAuth_UnknownCard_ShouldDeclineWithCardNotFound()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, Guid.NewGuid(), 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be("CARD_NOT_FOUND");
    }

    [Fact]
    public async Task DecideAuth_CardOfAnotherAccount_ShouldDeclineWithMismatch()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync();
        var (_, otherCard) = await CreateCreditAccountWithCardAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, otherCard.Id, 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse("a card may only draw on its own account");
        result.Reason.Should().Be("CARD_ACCOUNT_MISMATCH");
    }

    [Fact]
    public async Task DecideAuth_BlockedCardWithWrongPin_ShouldDeclineForStateBeforePin()
    {
        var (account, card) = await CreateCreditAccountWithCardAsync();
        await SetPinAsync(card.Id, "1234");
        card.Status = CardStatus.Blocked;
        await _db.SaveChangesAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, "9999", CancellationToken.None);

        result.Reason.Should().Be("CARD_BLOCKED", "card state is checked before the PIN so a blocked card never burns PIN retries");
        var persisted = _db.Cards.Single(x => x.Id == card.Id);
        persisted.PinRetryCount.Should().Be(0);
    }

    [Theory]
    [InlineData(AccountStatus.Blocked, "ACCOUNT_BLOCKED")]
    [InlineData(AccountStatus.Closed, "ACCOUNT_CLOSED")]
    [InlineData(AccountStatus.Delinquent, "ACCOUNT_DELINQUENT")]
    public async Task DecideAuth_AccountNotActive_ShouldDeclineEvenWithoutCard(AccountStatus status, string expectedReason)
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(accountStatus: status);

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse($"a {status} account must never be authorized");
        result.Reason.Should().Be(expectedReason);
    }

    [Theory]
    [InlineData("CARD_EXPIRED", "54")]
    [InlineData("CARD_EXPIRY_INVALID", "14")]
    [InlineData("CARD_NOT_FOUND", "14")]
    [InlineData("CARD_ACCOUNT_MISMATCH", "14")]
    [InlineData("CARD_CANCELLED", "14")]
    [InlineData("CARD_BLOCKED", "62")]
    [InlineData("CARD_NOT_ACTIVE", "62")]
    [InlineData("ACCOUNT_BLOCKED", "62")]
    [InlineData("ACCOUNT_CLOSED", "62")]
    [InlineData("ACCOUNT_DELINQUENT", "05")]
    [InlineData("INSUFFICIENT_AVAILABLE_CREDIT", "51")]
    public void MapResponseCode_StateDeclines_ShouldUseIso8583Codes(string reason, string expectedCode)
    {
        HoldResponseCodeCalculator.MapResponseCode(reason).Should().Be(expectedCode);
    }

    // ─────────────────────────────────────────────────────────
    // 1. PIN validation
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_InvalidPin_ShouldDeclineWithInvalidPin()
    {
        var (account, card) = await CreateCreditAccountWithCardAsync();
        await SetPinAsync(card.Id, "1234");

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, "9999", CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be("INVALID_PIN");
    }

    [Fact]
    public async Task DecideAuth_ValidPin_ShouldNotDeclineForPin()
    {
        var (account, card) = await CreateCreditAccountWithCardAsync();
        await SetPinAsync(card.Id, "1234");

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, "1234", CancellationToken.None);

        result.Reason.Should().NotBe("INVALID_PIN");
    }

    [Fact]
    public async Task DecideAuth_NoPinProvided_ShouldSkipPinCheck()
    {
        var (account, card) = await CreateCreditAccountWithCardAsync();
        await SetPinAsync(card.Id, "1234");

        var result = await _sut.DecideAuthAsync(
            account.Id, card.Id, 100m, null, null, null, CancellationToken.None);

        result.Reason.Should().NotBe("INVALID_PIN");
    }

    // ─────────────────────────────────────────────────────────
    // 2. Geography / country block
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_BlockedCountry_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync();
        _db.AntifraudRules.Add(new AntifraudRuleEntity
        {
            Id = Guid.NewGuid(),
            IsEnabled = true,
            Type = AntifraudRuleType.BlockCountry,
            TargetValue = "KP",
            RiskScore = 0,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 50m, null, "KP", null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Contain("COUNTRY_BLOCKED");
        result.Reason.Should().Contain("KP");
    }

    [Fact]
    public async Task DecideAuth_NonBlockedCountry_ShouldNotDeclineForCountry()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 50m, null, "EC", null, CancellationToken.None);

        result.Reason.Should().NotContain("COUNTRY_BLOCKED");
    }

    // ─────────────────────────────────────────────────────────
    // 3. MCC rules
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_BlockedMcc_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync();
        _db.MccRules.Add(new MccRuleEntity
        {
            Id = Guid.NewGuid(),
            Mcc = "7995",
            IsBlocked = true,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 100m, "7995", null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Contain("MCC_BLOCKED");
    }

    [Fact]
    public async Task DecideAuth_MccPerTxnLimitExceeded_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync();
        _db.MccRules.Add(new MccRuleEntity
        {
            Id = Guid.NewGuid(),
            Mcc = "5411",
            IsBlocked = false,
            PerTxnLimit = 200m,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 300m, "5411", null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Contain("MCC_PER_TXN_LIMIT");
    }

    [Fact]
    public async Task DecideAuth_MccPerTxnLimitNotExceeded_ShouldNotDeclineForMcc()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync();
        _db.MccRules.Add(new MccRuleEntity
        {
            Id = Guid.NewGuid(),
            Mcc = "5411",
            IsBlocked = false,
            PerTxnLimit = 500m,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 100m, "5411", null, null, CancellationToken.None);

        result.Reason.Should().NotContain("MCC");
    }

    // ─────────────────────────────────────────────────────────
    // 4. Account not found
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_UnknownAccount_ShouldDeclineWithAccountNotFound()
    {
        var result = await _sut.DecideAuthAsync(
            Guid.NewGuid(), null, 100m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be("ACCOUNT_NOT_FOUND");
    }

    // ─────────────────────────────────────────────────────────
    // 5. Available credit checks
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_InsufficientCredit_NoOverlimitPolicy_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 100m);

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 200m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be("INSUFFICIENT_AVAILABLE_CREDIT");
    }

    [Fact]
    public async Task DecideAuth_InsufficientCreditWithOverlimitAllowedAndWithinBuffer_ShouldApprove()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 100m, productCode: "OL_PROD");
        await AddCreditPolicyAsync("OL_PROD", allowOverlimit: true, overlimitBuffer: 50m);

        // 100 credit, amount = 130 → exceeds limit by 30, buffer = 50 → APPROVED
        var result = await _sut.DecideAuthAsync(
            account.Id, null, 130m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.Reason.Should().Be("OVERLIMIT_ALLOWED");
    }

    [Fact]
    public async Task DecideAuth_InsufficientCreditWithOverlimitBeyondBuffer_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 100m, productCode: "OL_PROD2");
        await AddCreditPolicyAsync("OL_PROD2", allowOverlimit: true, overlimitBuffer: 20m);

        // 100 credit, amount = 160 → exceeds buffer of 20 → DECLINED
        var result = await _sut.DecideAuthAsync(
            account.Id, null, 160m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be("INSUFFICIENT_AVAILABLE_CREDIT");
    }

    // ─────────────────────────────────────────────────────────
    // 6. Velocity rules
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_VelocityMaxCountExceeded_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 10000m, productCode: "VEL_PROD");
        await AddCreditPolicyAsync("VEL_PROD");

        _db.VelocityRules.Add(new VelocityRuleEntity
        {
            Id = Guid.NewGuid(),
            ProductCode = "VEL_PROD",
            WindowMinutes = 60,
            MaxCount = 2,
            MaxAmount = 999999m,
        });

        // Seed 2 existing txns within the window (at MaxCount limit)
        for (int i = 0; i < 2; i++)
        {
            _db.TxnJournal.Add(new TxnJournalEntity
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Amount = 10m,
                TxnType = SwitchTxnType.Authorization,
                PostedOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                Network = "VISA",
                Stan = $"S{i:000}",
                Rrn = $"R{i:000}",
                Mti = "0100",
            });
        }
        await _db.SaveChangesAsync();

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 50m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Contain("VELOCITY_MAX_COUNT");
    }

    [Fact]
    public async Task DecideAuth_VelocityMaxAmountExceeded_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 10000m, productCode: "VEL_AMT");
        await AddCreditPolicyAsync("VEL_AMT");

        _db.VelocityRules.Add(new VelocityRuleEntity
        {
            Id = Guid.NewGuid(),
            ProductCode = "VEL_AMT",
            WindowMinutes = 60,
            MaxCount = 999,
            MaxAmount = 500m,
        });

        _db.TxnJournal.Add(new TxnJournalEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Amount = 400m,
            TxnType = SwitchTxnType.Authorization,
            PostedOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            Network = "VISA",
            Stan = "S001",
            Rrn = "R001",
            Mti = "0100",
        });
        await _db.SaveChangesAsync();

        // 400 + 200 = 600 > 500 → DECLINE
        var result = await _sut.DecideAuthAsync(
            account.Id, null, 200m, null, null, null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Contain("VELOCITY_MAX_AMOUNT");
    }

    // ─────────────────────────────────────────────────────────
    // 7. Risk score / fraud heuristic
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_HighAmountPlusMonitoredCountry_WhenScoreReaches70_ShouldDecline()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 100000m, productCode: "FR_PROD");
        await AddCreditPolicyAsync("FR_PROD", floorLimit: 500m);

        // MonitorCountry rule adds 50 to risk score
        _db.AntifraudRules.Add(new AntifraudRuleEntity
        {
            Id = Guid.NewGuid(),
            IsEnabled = true,
            Type = AntifraudRuleType.MonitorCountry,
            TargetValue = "NG",
            RiskScore = 50,
        });
        await _db.SaveChangesAsync();

        // High amount >2x floorLimit(500) → +30 risk; MonitorCountry +50 → total 80 ≥ 70 → FRAUD
        var result = await _sut.DecideAuthAsync(
            account.Id, null, 1200m, null, "NG", null, CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.Reason.Should().Contain("FRAUD_SUSPECTED");
    }

    // ─────────────────────────────────────────────────────────
    // 8. Happy path
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAuth_ValidRequest_WithinAllLimits_ShouldApprove()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 5000m, productCode: "OK_PROD");
        await AddCreditPolicyAsync("OK_PROD");

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 100m, null, "US", null, CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.Reason.Should().BeOneOf("OK", "FLOOR_LIMIT_REVIEW");
    }

    [Fact]
    public async Task DecideAuth_FloorLimitExceeded_ShouldApproveWithReviewReason()
    {
        var (account, _) = await CreateCreditAccountWithCardAsync(creditLimit: 5000m, productCode: "FL_PROD");
        await AddCreditPolicyAsync("FL_PROD", floorLimit: 50m);

        var result = await _sut.DecideAuthAsync(
            account.Id, null, 100m, null, null, null, CancellationToken.None);

        // Amount ≥ floor limit → still Approved but reason = FLOOR_LIMIT_REVIEW
        result.Approved.Should().BeTrue();
        result.Reason.Should().Be("FLOOR_LIMIT_REVIEW");
    }
}
