using KobiFabrika.Domain.Common;

namespace KobiFabrika.Tests.Domain;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("0532 123 45 67", "+905321234567")]
    [InlineData("532-123-4567", "+905321234567")]
    [InlineData("+90 532 123 45 67", "+905321234567")]
    [InlineData("905321234567", "+905321234567")]
    [InlineData("(0212) 123 45 67", "+902121234567")]
    [InlineData("0850 123 45 67", "+908501234567")]
    public void Normalizes_to_e164(string input, string expected)
    {
        PhoneNumber.TryNormalize(input, out var e164).ShouldBeTrue();
        e164.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("0632 123 45 67")]
    [InlineData("+1 532 123 45 67")]
    [InlineData("+0532 123 45 67")]
    [InlineData("0532 123 45 6a")]
    [InlineData("05321234567890")]
    [InlineData("532+1234567")]
    public void Rejects_invalid_numbers(string? input) =>
        PhoneNumber.TryNormalize(input, out _).ShouldBeFalse();
}
