using FluentAssertions;
using IsoSwitch.Domain;

namespace IsoSwitch.Tests.Domain;

public sealed class CardDataMaskingTests
{
    private const string Pan = "4539578763621486";
    private const string MaskedPan = "453957******1486";
    private const string Track2 = "4539578763621486=29121011234567890";

    [Theory]
    [InlineData(Pan, MaskedPan)]
    [InlineData("4539578763621", "453957***3621")]                       // 13-digit PAN keeps 6 + 4
    [InlineData("4539578763621486123", "453957*********6123")]           // 19-digit PAN
    [InlineData("453957876362", "************")]                         // 12 chars: too short, fully masked
    [InlineData("1234", "****")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskPan_KeepsAtMostFirstSixAndLastFour(string? input, string expected)
    {
        CardDataMasking.MaskPan(input).Should().Be(expected);
    }

    [Fact]
    public void MaskTrack2_MasksThePanAndReplacesEverythingElse_PreservingLength()
    {
        var masked = CardDataMasking.MaskTrack2(Track2);

        masked.Should().HaveLength(Track2.Length);
        masked.Should().Be(MaskedPan + new string('*', Track2.Length - Pan.Length));
        masked.Should().NotContain("=").And.NotContain("2912");
    }

    [Theory]
    [InlineData(";4539578763621486=29121011234567890?", "*453957******1486*******************")]
    [InlineData("4539578763621486D29121011234567890", "453957******1486******************")]
    [InlineData("4539578763621486", MaskedPan)]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskTrack2_HandlesSentinelsHexSeparatorAndBarePan(string? input, string expected)
    {
        CardDataMasking.MaskTrack2(input).Should().Be(expected);
    }

    [Fact]
    public void MaskFields_RewritesOnlyTheSensitiveElements_AndLeavesTheInputUntouched()
    {
        var fields = new Dictionary<int, string>
        {
            [2] = Pan,
            [3] = "000000",
            [4] = "000000010050",
            [35] = Track2,
            [52] = "A1B2C3D4E5F60718",
            [55] = "9F2608AABBCCDD",
            [64] = "0011223344556677",
            [128] = "8899AABBCCDDEEFF"
        };

        var masked = CardDataMasking.MaskFields(fields);

        masked[2].Should().Be(MaskedPan);
        masked[3].Should().Be("000000");
        masked[4].Should().Be("000000010050");
        masked[35].Should().Be(CardDataMasking.MaskTrack2(Track2));
        masked[52].Should().Be(CardDataMasking.Redacted);
        masked[55].Should().Be(CardDataMasking.Redacted);
        masked[64].Should().Be(CardDataMasking.Redacted);
        masked[128].Should().Be(CardDataMasking.Redacted);
        masked.Should().HaveCount(fields.Count);

        // The outbound message must keep the real values.
        fields[2].Should().Be(Pan);
        fields[35].Should().Be(Track2);
        fields[52].Should().Be("A1B2C3D4E5F60718");
    }

    [Fact]
    public void SensitiveFields_IsTheUnionOfMaskedAndRedacted()
    {
        CardDataMasking.MaskedFields.Should().BeEquivalentTo(new[] { 2, 35 });
        CardDataMasking.RedactedFields.Should().BeEquivalentTo(new[] { 52, 55, 64, 128 });
        CardDataMasking.SensitiveFields.Should().BeEquivalentTo(new[] { 2, 35, 52, 55, 64, 128 });
    }

    [Fact]
    public void MaskFieldsJson_ReMasksTheAuthorizeShape_WithNestedFields()
    {
        var legacy = "{\"mti\":\"0100\",\"fields\":{\"2\":\"" + Pan + "\",\"4\":\"000000010050\",\"35\":\"" + Track2 + "\",\"52\":\"A1B2C3D4E5F60718\"}}";

        var masked = CardDataMasking.MaskFieldsJson(legacy)!;

        masked.Should().NotContain(Pan).And.NotContain(Track2).And.NotContain("A1B2C3D4E5F60718");
        masked.Should().Contain("\"2\":\"" + MaskedPan + "\"");
        masked.Should().Contain("\"4\":\"000000010050\"");
        masked.Should().Contain("\"52\":\"***\"");
        masked.Should().Contain("\"mti\":\"0100\"");
    }

    [Fact]
    public void MaskFieldsJson_ReMasksTheBareFieldMapShape()
    {
        var legacy = "{\"2\":\"" + Pan + "\",\"39\":\"00\",\"64\":\"0011223344556677\"}";

        var masked = CardDataMasking.MaskFieldsJson(legacy)!;

        masked.Should().Be("{\"2\":\"" + MaskedPan + "\",\"39\":\"00\",\"64\":\"***\"}");
    }

    [Fact]
    public void MaskFieldsJson_FailsClosedOnUnparseableInput_AndPassesNullThrough()
    {
        CardDataMasking.MaskFieldsJson("{not json " + Pan).Should().Be(CardDataMasking.Redacted);
        CardDataMasking.MaskFieldsJson(null).Should().BeNull();
        CardDataMasking.MaskFieldsJson("").Should().Be("");
    }

    [Fact]
    public void MaskFieldsJson_RedactsNonStringSensitiveValues()
    {
        var masked = CardDataMasking.MaskFieldsJson("{\"2\":4539578763621486,\"55\":{\"tag\":\"9F26\"}}")!;

        masked.Should().NotContain("4539578763621486").And.NotContain("9F26");
        masked.Should().Be("{\"2\":\"***\",\"55\":\"***\"}");
    }

    [Fact]
    public void ExtractMaskedPan_ReturnsTheMaskedDe2_FromEitherShape()
    {
        CardDataMasking.ExtractMaskedPan("{\"mti\":\"0100\",\"fields\":{\"2\":\"" + Pan + "\"}}").Should().Be(MaskedPan);
        CardDataMasking.ExtractMaskedPan("{\"2\":\"" + Pan + "\"}").Should().Be(MaskedPan);
        CardDataMasking.ExtractMaskedPan("{\"4\":\"000000010050\"}").Should().BeNull();
        CardDataMasking.ExtractMaskedPan(null).Should().BeNull();
        CardDataMasking.ExtractMaskedPan("not json").Should().BeNull();
    }

    [Theory]
    [InlineData(Pan, true)]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("4539 5787 6362 1486", true)]   // separators are ignored
    [InlineData("12345678901", false)]          // fewer than 12 digits
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidLuhn_MatchesKnownVectors(string? pan, bool expected)
    {
        CardDataMasking.IsValidLuhn(pan).Should().Be(expected);
    }
}
