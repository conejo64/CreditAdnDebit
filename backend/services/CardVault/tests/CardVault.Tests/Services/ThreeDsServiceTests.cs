using BuildingBlocks.Outbox;
using CardVault.Api.Contracts;
using CardVault.Api.Pci;
using CardVault.Api.Services;
using CardVault.Api.Vault;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Ecommerce;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace CardVault.Tests.Services;

/// <summary>
/// Unit tests for ThreeDsService covering the full 3DS challenge lifecycle:
/// StartChallenge → VerifyChallenge (approve / wrong OTP / lockout / expired).
///
/// Dependencies that hit external infrastructure (Kafka, notifications) are
/// replaced with NSubstitute mocks so the tests run fully in-process.
/// </summary>
public sealed class ThreeDsServiceTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly ThreeDsService _sut;

    public ThreeDsServiceTests()
    {
        _db = TestDbContextFactory.Create();

        var busMock = Substitute.For<IEventBus>();
        busMock.PublishAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var pciAudit = new PciAuditPublisher(busMock);
        var audit    = new AuditService(_db);
        var crypto   = TestVaultCrypto.Create();
        var notifications = new NotificationService(_db, audit, pciAudit, crypto);

        // IsDevelopment() = true so DevelopmentOtp is returned in the response
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        _sut = new ThreeDsService(_db, audit, pciAudit, env, notifications);
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private async Task<(CustomerEntity Customer, CardAccountEntity Account, CardEntity Card)> SeedAsync(
        CardStatus cardStatus = CardStatus.Active,
        AccountStatus accountStatus = AccountStatus.Active)
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id             = Guid.NewGuid(),
            FullName       = "3DS Test User",
            DocumentId     = $"DOC{Guid.NewGuid():N}"[..10],
            Email          = "testuser@example.com",
            Phone          = "+593999000099",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = _db.Accounts.Add(new CardAccountEntity
        {
            Id             = Guid.NewGuid(),
            CustomerId     = customer.Id,
            AccountType    = AccountType.Credit,
            ProductCode    = "VISA_CLASSIC",
            CreditLimit    = 5000m,
            AvailableLimit = 5000m,
            AccountNumber  = $"ACC{Guid.NewGuid():N}"[..10],
            Status         = accountStatus,
            CurrencyCode   = "USD",
        }).Entity;

        var card = _db.Cards.Add(new CardEntity
        {
            Id         = Guid.NewGuid(),
            AccountId  = account.Id,
            Bin        = "411111",
            PanToken   = $"tok_{Guid.NewGuid():N}",
            MaskedPan  = "411111******1111",
            Last4      = "1111",
            ExpiryYyMm = "2812",
            Status     = cardStatus,
        }).Entity;

        await _db.SaveChangesAsync();
        return (customer, account, card);
    }

    private static StartThreeDsChallengeRequest BuildRequest(Guid cardId, decimal amount = 100m,
        string merchantCountry = "US", string browserCountry = "US")
        => new(
            CardId:          cardId,
            Amount:          amount,
            Currency:        "USD",
            MerchantId:      "MCH001",
            MerchantName:    "Test Store",
            MerchantCountry: merchantCountry,
            BrowserIpCountry: browserCountry,
            DeviceChannel:   "BROWSER");

    // ─────────────────────────────────────────────────────────────
    // StartChallengeAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartChallenge_ValidCard_ShouldCreatePendingChallenge()
    {
        var (_, _, card) = await SeedAsync();

        var response = await _sut.StartChallengeAsync(BuildRequest(card.Id), "test-actor", "trace-001", CancellationToken.None);

        response.Should().NotBeNull();
        response.Status.Should().Be("PENDING");
        response.Decision.Should().Be("PENDING");
        response.CardId.Should().Be(card.Id);

        var entity = await _db.ThreeDsChallenges.FirstOrDefaultAsync(x => x.CardId == card.Id);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(ThreeDsChallengeStatus.Pending);
    }

    [Fact]
    public async Task StartChallenge_ShouldReturnOtpInDevelopment()
    {
        var (_, _, card) = await SeedAsync();

        var response = await _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-otp", CancellationToken.None);

        response.DevelopmentOtp.Should().NotBeNullOrWhiteSpace("OTP must be exposed in dev for testing");
        response.DevelopmentOtp!.Length.Should().Be(6);
        response.DevelopmentOtp.Should().MatchRegex(@"^\d{6}$");
    }

    [Fact]
    public async Task StartChallenge_InactiveCard_ShouldThrow()
    {
        var (_, _, card) = await SeedAsync(cardStatus: CardStatus.Blocked);

        var act = () => _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-err", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active cards*");
    }

    [Fact]
    public async Task StartChallenge_InactiveAccount_ShouldThrow()
    {
        var (_, _, card) = await SeedAsync(accountStatus: AccountStatus.Closed);

        var act = () => _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-err2", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active accounts*");
    }

    [Fact]
    public async Task StartChallenge_ZeroAmount_ShouldThrow()
    {
        var (_, _, card) = await SeedAsync();

        var act = () => _sut.StartChallengeAsync(BuildRequest(card.Id, amount: 0), "actor", "trace-zero", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Amount must be greater*");
    }

    [Fact]
    public async Task StartChallenge_UnknownCard_ShouldThrow()
    {
        var act = () => _sut.StartChallengeAsync(BuildRequest(Guid.NewGuid()), "actor", "trace-notfound", CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task StartChallenge_HighAmount_ShouldHaveElevatedRiskScore()
    {
        var (_, _, card) = await SeedAsync();

        var response = await _sut.StartChallengeAsync(BuildRequest(card.Id, amount: 1500m), "actor", "trace-risk", CancellationToken.None);

        // base=15 + amount>=250→+20 + amount>=1000→+10 = at least 45
        response.RiskScore.Should().BeGreaterThanOrEqualTo(45);
        response.RiskReasons.Should().Contain("amount_above_standard_ticket");
    }

    [Fact]
    public async Task StartChallenge_CountryMismatch_ShouldAddRiskReason()
    {
        var (_, _, card) = await SeedAsync();

        var response = await _sut.StartChallengeAsync(
            BuildRequest(card.Id, merchantCountry: "BR", browserCountry: "US"),
            "actor", "trace-country", CancellationToken.None);

        response.RiskReasons.Should().Contain("country_mismatch_between_browser_and_merchant");
    }

    // ─────────────────────────────────────────────────────────────
    // VerifyChallengeAsync — happy path
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyChallenge_CorrectOtp_ShouldAuthenticate()
    {
        var (_, _, card) = await SeedAsync();
        var started = await _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-verify", CancellationToken.None);
        var otp = started.DevelopmentOtp!;

        var result = await _sut.VerifyChallengeAsync(started.ChallengeId, otp, "actor", "trace-verify", CancellationToken.None);

        result.Status.Should().Be("AUTHENTICATED");
        result.Decision.Should().Be("APPROVE");
        result.DecisionReason.Should().Be("authenticated");
        result.AttemptsUsed.Should().Be(1);
        result.AttemptsRemaining.Should().Be(2);
    }

    [Fact]
    public async Task VerifyChallenge_CorrectOtp_ShouldPersistAuthenticatedStatus()
    {
        var (_, _, card) = await SeedAsync();
        var started = await _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-persist", CancellationToken.None);

        await _sut.VerifyChallengeAsync(started.ChallengeId, started.DevelopmentOtp!, "actor", "trace-persist", CancellationToken.None);

        var entity = await _db.ThreeDsChallenges.FirstAsync(x => x.Id == started.ChallengeId);
        entity.Status.Should().Be(ThreeDsChallengeStatus.Authenticated);
        entity.Decision.Should().Be(ThreeDsDecision.Approve);
        entity.AuthenticatedOn.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────
    // VerifyChallengeAsync — wrong OTP
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyChallenge_WrongOtp_ShouldIncrementAttempts()
    {
        var (_, _, card) = await SeedAsync();
        var started = await _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-wrong", CancellationToken.None);

        var result = await _sut.VerifyChallengeAsync(started.ChallengeId, "000000", "actor", "trace-wrong", CancellationToken.None);

        result.Status.Should().Be("PENDING");
        result.Decision.Should().Be("PENDING");
        result.DecisionReason.Should().Be("invalid_otp");
        result.AttemptsUsed.Should().Be(1);
        result.AttemptsRemaining.Should().Be(2);
    }

    [Fact]
    public async Task VerifyChallenge_MaxWrongAttempts_ShouldLockout()
    {
        var (_, _, card) = await SeedAsync();
        var started = await _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-lock", CancellationToken.None);
        const string wrongOtp = "000000";

        await _sut.VerifyChallengeAsync(started.ChallengeId, wrongOtp, "actor", "trace-lock", CancellationToken.None);
        await _sut.VerifyChallengeAsync(started.ChallengeId, wrongOtp, "actor", "trace-lock", CancellationToken.None);
        var result = await _sut.VerifyChallengeAsync(started.ChallengeId, wrongOtp, "actor", "trace-lock", CancellationToken.None);

        result.Status.Should().Be("REJECTED");
        result.Decision.Should().Be("REJECT");
        result.DecisionReason.Should().Be("otp_attempts_exhausted");
        result.AttemptsRemaining.Should().Be(0);
    }

    [Fact]
    public async Task VerifyChallenge_AfterLockout_ShouldNotIncrementAttempts()
    {
        var (_, _, card) = await SeedAsync();
        var started = await _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-post-lock", CancellationToken.None);
        const string wrongOtp = "000000";

        for (var i = 0; i < 3; i++)
            await _sut.VerifyChallengeAsync(started.ChallengeId, wrongOtp, "actor", "trace-post-lock", CancellationToken.None);

        // Extra attempt after lockout — must short-circuit, not process
        var postLock = await _sut.VerifyChallengeAsync(started.ChallengeId, wrongOtp, "actor", "trace-post-lock", CancellationToken.None);

        postLock.Status.Should().Be("REJECTED");
        var entity = await _db.ThreeDsChallenges.FirstAsync(x => x.Id == started.ChallengeId);
        entity.OtpAttempts.Should().Be(3, "attempts must not exceed MaxAttempts after lockout");
    }

    // ─────────────────────────────────────────────────────────────
    // VerifyChallengeAsync — expiry
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyChallenge_ExpiredChallenge_ShouldReject()
    {
        var (_, _, card) = await SeedAsync();
        var started = await _sut.StartChallengeAsync(BuildRequest(card.Id), "actor", "trace-exp", CancellationToken.None);

        // Force-expire by mutating the entity directly in the DB
        var entity = await _db.ThreeDsChallenges.FirstAsync(x => x.Id == started.ChallengeId);
        entity.ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(-10);
        await _db.SaveChangesAsync();

        var result = await _sut.VerifyChallengeAsync(started.ChallengeId, started.DevelopmentOtp!, "actor", "trace-exp", CancellationToken.None);

        result.Status.Should().Be("EXPIRED");
        result.Decision.Should().Be("REJECT");
        result.DecisionReason.Should().Be("challenge_expired");
    }

    // ─────────────────────────────────────────────────────────────
    // VerifyChallengeAsync — not found
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyChallenge_UnknownChallenge_ShouldThrow()
    {
        var act = () => _sut.VerifyChallengeAsync(Guid.NewGuid(), "123456", "actor", "trace-nf", CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
