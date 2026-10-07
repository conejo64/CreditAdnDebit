using System.Text.Json;
using FluentAssertions;
using IsoSwitch.Application.Ports;
using IsoSwitch.Domain;
using IsoSwitch.Infrastructure.Messaging;
using IsoSwitch.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IsoSwitch.Tests.Infrastructure.Messaging;

/// <summary>
/// The binary audit path used to publish and persist the raw ISO 8583 frame as hex, which is the
/// PAN, track 2 and PIN block in clear. Nothing downstream parses that hex, so the frame is
/// reduced to metadata (length + SHA-256) and the field view is masked.
/// </summary>
public sealed class BinaryIsoAuditServiceTests
{
    private const string Pan = "4539578763621486";
    private const string Track2 = "4539578763621486=29121011234567890";

    private sealed class CapturingPublisher : ISwitchEventPublisher
    {
        public List<string> AuditPayloads { get; } = new();
        public Task PublishTxAsync(string key, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task PublishIsoAsync(string key, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task PublishAuditAsync(string key, object payload, CancellationToken ct)
        {
            AuditPayloads.Add(JsonSerializer.Serialize(payload));
            return Task.CompletedTask;
        }
    }

    private static IConfiguration WriteToDbEnabled() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:WriteToDb"] = "true" })
        .Build();

    [Fact]
    public async Task LogAsync_MasksCardData_InBothTheKafkaEnvelopeAndTheDatabaseRow()
    {
        using var db = TestDbContextFactory.Create();
        var publisher = new CapturingPublisher();
        var sut = new BinaryIsoAuditService(db, publisher, WriteToDbEnabled());

        var msg = new IsoMessage { Mti = "0200" };
        msg.Set(2, Pan);
        msg.Set(11, "123456");
        msg.Set(35, Track2);
        msg.Set(37, "ABCDEF123456");
        msg.Set(52, "A1B2C3D4E5F60718");

        await sut.LogAsync("trace-bin-1", "IN", msg, CancellationToken.None);

        publisher.AuditPayloads.Should().ContainSingle();
        publisher.AuditPayloads[0].Should().NotContain(Pan).And.NotContain(Track2).And.NotContain("A1B2C3D4E5F60718");
        publisher.AuditPayloads[0].Should().Contain("453957******1486");

        var row = await db.IsoMessageLogs.AsNoTracking().SingleAsync(l => l.TraceId == "trace-bin-1");
        row.FieldsJson.Should().NotContain(Pan).And.NotContain(Track2).And.NotContain("A1B2C3D4E5F60718");
        row.FieldsJson.Should().Contain("453957******1486");
    }

    [Fact]
    public async Task LogFrameAsync_NeverPublishesOrPersistsTheRawFrame_OnlyLengthAndDigest()
    {
        using var db = TestDbContextFactory.Create();
        var publisher = new CapturingPublisher();
        var sut = new BinaryIsoAuditService(db, publisher, WriteToDbEnabled());

        // An ASCII-encoded frame makes the leak observable as plain text in JSON.
        var frame = System.Text.Encoding.ASCII.GetBytes("0100" + Pan + Track2 + "A1B2C3D4E5F60718");
        var tpdu = new byte[] { 0x60, 0x00, 0x01, 0x00, 0x00 };
        var frameHex = Convert.ToHexString(frame);
        var expectedDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame));

        await sut.LogFrameAsync("stan|rrn", "IN", "0100", "123456", "ABCDEF123456", frame, tpdu,
            new { tokenPan = (string?)null, amount = "000000010050" }, CancellationToken.None);

        publisher.AuditPayloads.Should().ContainSingle();
        var published = publisher.AuditPayloads[0];
        published.Should().NotContain(frameHex).And.NotContain(Pan).And.NotContain(Track2);
        published.Should().NotContain("payloadHex").And.NotContain("tpduHex");
        published.Should().Contain(expectedDigest);
        published.Should().Contain($"\"frameLength\":{frame.Length}");
        published.Should().Contain($"\"tpduLength\":{tpdu.Length}");

        var row = await db.IsoMessageLogs.AsNoTracking().SingleAsync(l => l.TraceId == "stan|rrn");
        row.FieldsJson.Should().NotContain(frameHex).And.NotContain(Pan).And.NotContain(Track2);
        row.FieldsJson.Should().NotContain("payloadHex");
        row.FieldsJson.Should().Contain(expectedDigest);
        row.FieldsJson.Should().Contain("000000010050");
    }
}
