using FluentAssertions;
using IsoSwitch.Domain;
using IsoSwitch.Infrastructure.Messaging;
using IsoSwitch.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Tests.Infrastructure.Messaging;

/// <summary>
/// The audit log is read by operators through <c>/api/iso/logs/{traceId}</c>; whatever is written
/// here is effectively displayed. Card data must therefore be masked before it is persisted,
/// including track 2 (DE35), which embeds the PAN and was previously logged in clear.
/// </summary>
public sealed class IsoAuditServiceTests
{
    private const string Pan = "4539578763621486";
    private const string Track2 = "4539578763621486=29121011234567890";

    [Fact]
    public async Task LogAsync_MasksPanAndTrack2_AndRedactsPinEmvAndMac()
    {
        using var db = TestDbContextFactory.Create();
        var sut = new IsoAuditService(db);

        var msg = new IsoMessage { Mti = "0100" };
        msg.Set(2, Pan);
        msg.Set(4, "000000010050");
        msg.Set(35, Track2);
        msg.Set(52, "A1B2C3D4E5F60718");
        msg.Set(55, "9F2608AABBCCDD");
        msg.Set(64, "0011223344556677");
        msg.Set(128, "8899AABBCCDDEEFF");

        await sut.LogAsync("trace-audit-1", "OUT", msg, CancellationToken.None);

        var row = await db.IsoMessageLogs.AsNoTracking().SingleAsync(l => l.TraceId == "trace-audit-1");
        row.Mti.Should().Be("0100");
        row.FieldsJson.Should().NotContain(Pan);
        row.FieldsJson.Should().NotContain(Track2);
        row.FieldsJson.Should().NotContain("=2912");
        row.FieldsJson.Should().NotContain("A1B2C3D4E5F60718");
        row.FieldsJson.Should().NotContain("9F2608AABBCCDD");
        row.FieldsJson.Should().NotContain("0011223344556677");
        row.FieldsJson.Should().NotContain("8899AABBCCDDEEFF");

        // Diagnostic value is preserved: masked PAN and the non-sensitive amount survive.
        row.FieldsJson.Should().Contain("453957******1486");
        row.FieldsJson.Should().Contain("000000010050");
    }
}
