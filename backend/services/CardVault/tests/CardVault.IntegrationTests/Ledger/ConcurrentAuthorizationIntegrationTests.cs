using CardVault.Application.Ports;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.IntegrationTests.Ledger;

/// <summary>
/// Gate 0 / T5 on real PostgreSQL: the available-credit check and the hold insert in
/// <see cref="HoldService.AuthorizeAsync"/> are atomic per account. Two authorizations racing on
/// separate connections, whose sum exceeds the limit, can never both be approved.
/// </summary>
public sealed class ConcurrentAuthorizationIntegrationTests : IntegrationTestBase
{
    private const int Rounds = 5;

    public ConcurrentAuthorizationIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
    }

    [Fact(DisplayName = "Two parallel authorizations of 600 on a 1000 limit yield exactly one approved hold")]
    public async Task Parallel_authorizations_cannot_jointly_exceed_the_limit()
    {
        // The race is probabilistic per attempt; several rounds on fresh accounts make a missing
        // lock observable, while a correct lock must pass every round.
        for (var round = 0; round < Rounds; round++)
        {
            var account = await SeedCreditAccountAsync(creditLimit: 1000m);

            await using var contextA = PostgresFixture.CreateSiblingContext(Db);
            await using var contextB = PostgresFixture.CreateSiblingContext(Db);
            using var providerA = BuildServices(contextA);
            using var providerB = BuildServices(contextB);

            var holdsA = providerA.GetRequiredService<HoldService>();
            var holdsB = providerB.GetRequiredService<HoldService>();
            var postedOn = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

            var taskA = Task.Run(() => holdsA.AuthorizeAsync(account.Id, null, "VISA", "0100", $"A{round:000}", $"RRNA{round:000}", null,
                "MERCHANT01", "5411", null, null, 600m, postedOn, CancellationToken.None));
            var taskB = Task.Run(() => holdsB.AuthorizeAsync(account.Id, null, "VISA", "0100", $"B{round:000}", $"RRNB{round:000}", null,
                "MERCHANT02", "5411", null, null, 600m, postedOn, CancellationToken.None));

            var outcomes = await Task.WhenAll(Observe(taskA), Observe(taskB));

            var approved = outcomes.Count(o => o.Approved);
            var declines = outcomes.Where(o => !o.Approved).Select(o => o.Reason).ToList();

            await using var reader = PostgresFixture.CreateSiblingContext(Db);
            var activeHolds = await reader.AuthorizationHolds.AsNoTracking()
                .Where(x => x.AccountId == account.Id && x.Status == HoldStatus.Active)
                .SumAsync(x => x.Amount - x.CapturedAmount);
            var credit = await new AvailableCreditService(reader).GetAsync(account.Id, CancellationToken.None);

            approved.Should().Be(1, $"round {round}: exactly one of two 600 authorizations fits in a 1000 limit (declines: {string.Join(", ", declines)})");
            declines.Should().ContainSingle().Which.Should().Contain("INSUFFICIENT_AVAILABLE_CREDIT");
            activeHolds.Should().Be(600m, $"round {round}: pending holds never exceed the credit limit");
            credit.AvailableCredit.Should().Be(400m, $"round {round}: available credit is limit minus the single approved hold");
            (credit.CreditLimit - credit.PostedBalance - credit.ActiveHolds).Should().BeGreaterThanOrEqualTo(0m, "available credit is never negative");
        }
    }

    private static async Task<(bool Approved, string Reason)> Observe(Task<AuthorizationHoldEntity> authorization)
    {
        try
        {
            await authorization;
            return (true, "APPROVED");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("AUTH_DECLINED", StringComparison.Ordinal))
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Mirrors the DI graph <c>CardVault.Tests.Services.HoldServiceTests</c> wires, with a no-op
    /// decision publisher so Kafka is never touched. One provider per connection.
    /// </summary>
    private static ServiceProvider BuildServices(CardVaultDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<AuditService>();
        services.AddSingleton<AvailableCreditService>();
        services.AddSingleton<PinService>();
        services.AddSingleton<CreditPolicyService>();
        services.AddSingleton<RiskDecisionService>();
        services.AddSingleton<CreditLimitManagementService>();
        services.AddSingleton<IAuthDecisionPublisher, NoOpAuthDecisionPublisher>();
        services.AddSingleton(provider => new HoldService(
            provider.GetRequiredService<CardVaultDbContext>(),
            provider.GetRequiredService<AuditService>(),
            provider));
        return services.BuildServiceProvider();
    }

    private sealed class NoOpAuthDecisionPublisher : IAuthDecisionPublisher
    {
        public Task PublishAuthResponseAsync(string key, object payload, CancellationToken ct) => Task.CompletedTask;
    }
}
