using CardVault.Domain;
using FluentAssertions;

namespace CardVault.Tests.Domain;

/// <summary>
/// Gate 0 / T9: the Luhn check digit is the integrity rule every acquirer and network applies to a PAN.
/// The Domain implementation is pure and must agree with the published vectors below.
/// </summary>
public sealed class LuhnTests
{
    [Theory]
    [InlineData("4539578763621486")]
    [InlineData("4111111111111111")]
    [InlineData("5555555555554444")]
    [InlineData("378282246310005")]
    public void IsValid_accepts_known_valid_numbers(string pan)
    {
        Luhn.IsValid(pan).Should().BeTrue();
    }

    [Theory]
    [InlineData("4539578763621487")]
    [InlineData("4111111111111112")]
    [InlineData("1234567890123456")]
    public void IsValid_rejects_numbers_with_a_wrong_check_digit(string pan)
    {
        Luhn.IsValid(pan).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("4539 5787 6362 1486")]
    [InlineData("453957876362148x")]
    public void IsValid_rejects_input_that_is_not_a_digit_string_of_at_least_two_characters(string input)
    {
        Luhn.IsValid(input).Should().BeFalse();
    }

    [Theory]
    [InlineData("453957876362148", '6')]
    [InlineData("411111111111111", '1')]
    [InlineData("555555555555444", '4')]
    [InlineData("7992739871", '3')]
    public void ComputeCheckDigit_returns_the_digit_that_makes_the_number_valid(string partial, char expected)
    {
        var digit = Luhn.ComputeCheckDigit(partial);

        digit.Should().Be(expected);
        Luhn.IsValid(partial + digit).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12a4")]
    public void ComputeCheckDigit_rejects_input_that_is_not_a_digit_string(string partial)
    {
        var act = () => Luhn.ComputeCheckDigit(partial);

        act.Should().Throw<ArgumentException>();
    }
}
