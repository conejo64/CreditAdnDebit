using CardVault.Application.Services;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

public sealed class AuditServiceTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly AuditService _sut;

    public AuditServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _sut = new AuditService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task WriteAsync_ShouldPersistEvent_WithSha256Hash()
    {
        // Act
        await _sut.WriteAsync("test.event", new { key = "value" }, "corr-123", "trace-456", CancellationToken.None);

        // Assert
        var events = await _sut.LatestAsync(10, CancellationToken.None);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("test.event");
        events[0].Service.Should().Be("CardVault");
        events[0].CorrelationId.Should().Be("corr-123");
        events[0].TraceId.Should().Be("trace-456");
        events[0].PayloadSha256.Should().NotBeNullOrWhiteSpace("SHA256 hash should be computed");
        events[0].PayloadJson.Should().Contain("\"key\":\"value\"");
    }

    [Fact]
    public async Task WriteAsync_SamePayload_ShouldProduceSameHash()
    {
        // Arrange
        var payload = new { amount = 100, currency = "USD" };

        // Act
        await _sut.WriteAsync("event.1", payload, null, null, CancellationToken.None);
        await _sut.WriteAsync("event.2", payload, null, null, CancellationToken.None);

        // Assert
        var events = await _sut.LatestAsync(10, CancellationToken.None);
        events[0].PayloadSha256.Should().Be(events[1].PayloadSha256, "identical payloads must produce identical hashes");
    }

    [Fact]
    public async Task LatestAsync_ShouldReturnOrderedByOccurredOnDesc()
    {
        // Arrange
        await _sut.WriteAsync("first", new { }, null, null, CancellationToken.None);
        await Task.Delay(10); // small delay to ensure different timestamps
        await _sut.WriteAsync("second", new { }, null, null, CancellationToken.None);
        await Task.Delay(10);
        await _sut.WriteAsync("third", new { }, null, null, CancellationToken.None);

        // Act
        var events = await _sut.LatestAsync(10, CancellationToken.None);

        // Assert
        events.Should().HaveCount(3);
        events[0].EventType.Should().Be("third");
        events[2].EventType.Should().Be("first");
    }

    [Fact]
    public async Task LatestAsync_ShouldRespectTakeLimit()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
            await _sut.WriteAsync($"event.{i}", new { }, null, null, CancellationToken.None);

        // Act
        var events = await _sut.LatestAsync(3, CancellationToken.None);

        // Assert
        events.Should().HaveCount(3);
    }

    [Fact]
    public async Task WriteAsync_NullCorrelationAndTrace_ShouldNotThrow()
    {
        // Act
        var act = async () => await _sut.WriteAsync("nullable.test", new { ok = true }, null, null, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
