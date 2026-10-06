using FluentAssertions;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.IntegrationTests.Persistence;

/// <summary>
/// Behaviour smoke test over the migrated schema: a <see cref="TransactionEntity"/> and its
/// <see cref="IsoMessageLogEntity"/> pair round-trip through the real PostgreSQL tables created by
/// <c>Migrate()</c>, including the unique indexes the InMemory provider never enforces.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait(Category.TraitName, Category.Integration)]
public sealed class TransactionPersistenceSmokeTest
{
    private readonly PostgresFixture _fixture;

    public TransactionPersistenceSmokeTest(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "A transaction and its ISO message log round-trip through the migrated schema")]
    public async Task Transaction_and_message_log_round_trip()
    {
        var options = await _fixture.CreateOptionsAsync();
        await using var db = new IsoSwitchDbContext(options);
        await db.Database.MigrateAsync();

        var traceId = $"trace-{Guid.NewGuid():N}";
        var createdOn = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

        db.Transactions.Add(new TransactionEntity
        {
            TraceId = traceId,
            Stan = "123456",
            ConnectorId = "SIM",
            RequestJson = "{\"2\":\"****\"}",
            Amount12 = "000000010000",
            Currency = "840",
            Status = "COMPLETED",
            ResponseCode = "00",
            CreatedOn = createdOn,
            CompletedOn = createdOn.AddMilliseconds(250)
        });
        db.IsoMessageLogs.Add(new IsoMessageLogEntity
        {
            TraceId = traceId,
            Direction = "OUT",
            Mti = "0100",
            FieldsJson = "{}",
            CreatedOn = createdOn
        });
        db.IsoMessageLogs.Add(new IsoMessageLogEntity
        {
            TraceId = traceId,
            Direction = "IN",
            Mti = "0110",
            FieldsJson = "{\"39\":\"00\"}",
            CreatedOn = createdOn.AddMilliseconds(250)
        });
        await db.SaveChangesAsync();

        await using var reader = PostgresFixture.CreateSiblingContext(db);

        var transaction = await reader.Transactions.AsNoTracking().SingleAsync(t => t.TraceId == traceId);
        transaction.Status.Should().Be("COMPLETED");
        transaction.ResponseCode.Should().Be("00");
        transaction.Amount12.Should().Be("000000010000");
        transaction.CreatedOn.Should().Be(createdOn);
        transaction.CompletedOn.Should().Be(createdOn.AddMilliseconds(250));

        var logs = await reader.IsoMessageLogs.AsNoTracking()
            .Where(l => l.TraceId == traceId)
            .OrderBy(l => l.CreatedOn)
            .ToListAsync();
        logs.Select(l => (l.Direction, l.Mti)).Should().Equal(("OUT", "0100"), ("IN", "0110"));

        // Unique index on (TraceId, Direction) is real on PostgreSQL.
        reader.IsoMessageLogs.Add(new IsoMessageLogEntity
        {
            TraceId = traceId,
            Direction = "OUT",
            Mti = "0100",
            FieldsJson = "{}",
            CreatedOn = createdOn
        });
        var duplicate = async () => await reader.SaveChangesAsync();
        (await duplicate.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23505");
    }
}
