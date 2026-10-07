using CardVault.Application.Services;
using CardVault.Domain;
using FluentAssertions;

namespace CardVault.Tests.Features.Issuer;

/// <summary>
/// Gate 0 / T9: PANs are generated server-side from a BIN with a cryptographically secure RNG and a Luhn
/// check digit. These tests pin the pure generation and BIN-range matching rules; the database-backed
/// range lookup and collision retry live in <see cref="IssuerService"/>.
/// </summary>
public sealed class PanGeneratorTests
{
    [Theory]
    [InlineData("411111")]
    [InlineData("45397000")]
    public void Generate_returns_a_16_digit_Luhn_valid_PAN_that_starts_with_the_BIN(string bin)
    {
        for (var i = 0; i < 200; i++)
        {
            var pan = PanGenerator.Generate(bin);

            pan.Should().HaveLength(16);
            pan.Should().StartWith(bin);
            pan.Should().MatchRegex("^[0-9]{16}$");
            Luhn.IsValid(pan).Should().BeTrue($"'{pan}' must carry a correct check digit");
        }
    }

    [Fact]
    public void Generate_does_not_repeat_the_same_PAN_across_many_draws()
    {
        var pans = Enumerable.Range(0, 500).Select(_ => PanGenerator.Generate("411111")).ToHashSet();

        // 9 free digits give a billion candidates; 500 draws colliding would mean a broken RNG.
        pans.Should().HaveCount(500);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4111")]
    [InlineData("4111111")]
    [InlineData("411111111")]
    [InlineData("41111a")]
    [InlineData("411 111")]
    public void Generate_rejects_a_BIN_that_is_not_6_or_8_digits(string bin)
    {
        var act = () => PanGenerator.Generate(bin);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_reserves_the_19_digit_length_without_implementing_it()
    {
        var act = () => PanGenerator.Generate("411111", totalLength: 19);

        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("411111", 400000, 499999, true)]
    [InlineData("400000", 400000, 499999, true)]
    [InlineData("499999", 400000, 499999, true)]
    [InlineData("500000", 400000, 499999, false)]
    [InlineData("399999", 400000, 499999, false)]
    [InlineData("41111100", 400000, 499999, true)]
    [InlineData("41111100", 41111100, 41111199, true)]
    [InlineData("41111200", 41111100, 41111199, false)]
    [InlineData("411111", 41111100, 41111199, false)]
    public void BinMatchesRange_compares_the_numeric_prefix_at_the_width_of_the_range(string bin, int binStart, int binEnd, bool expected)
    {
        PanGenerator.BinMatchesRange(bin, binStart, binEnd).Should().Be(expected);
    }
}
