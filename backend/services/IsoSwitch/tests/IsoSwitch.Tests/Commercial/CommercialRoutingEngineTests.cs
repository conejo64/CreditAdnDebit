using BuildingBlocks.Commercial;
using FluentAssertions;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using IsoSwitch.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Tests.Commercial;

public class CommercialRoutingEngineTests : IDisposable
{
    private readonly IsoSwitchDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CommercialMode_WithoutRoutingRule_FailsBeforeSimulatorFallback()
    {
        _db.BinRangesCache.Add(new BinRangeCacheEntity
        {
            Id = Guid.NewGuid(),
            BinStart = 411111,
            BinEnd = 411111,
            CountryCode = "EC",
            Brand = "VISA",
            Product = "CREDIT",
            Enabled = true
        });
        await _db.SaveChangesAsync();
        var router = new RoutingEngineV2(_db, Options.Create(new CommercialOptions { Mode = CommercialMode.Commercial }));

        var act = () => router.ResolveAsync(411111, "EC", "VISA", "AUTH", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*commercial mode*route*");
    }

    [Fact]
    public async Task DemoMode_WithoutRoutingRule_PreservesSimulatorFallback()
    {
        var router = new RoutingEngineV2(_db, Options.Create(new CommercialOptions { Mode = CommercialMode.Demo, EnableDemoSurfaces = true }));

        var decision = await router.ResolveAsync(411111, null, null, "AUTH", CancellationToken.None);

        decision.ConnectorId.Should().Be("SIMULATOR");
        decision.Mode.Should().Be("DEMO_FALLBACK");
    }
}


