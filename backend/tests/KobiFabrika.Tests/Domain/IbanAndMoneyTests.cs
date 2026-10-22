using KobiFabrika.Domain.Common;

namespace KobiFabrika.Tests.Domain;

/// <summary>IBAN ve para kuralları; aynı örnekler frontend src/lib/validation/rules.test.ts'te de var.</summary>
public sealed class IbanAndMoneyTests
{
    [Theory]
    [InlineData("TR400006200000000000000001", "TR400006200000000000000001")]
    [InlineData("tr83 0001 0000 0000 0123 4567 89", "TR830001000000000123456789")]
    [InlineData(" TR10-0004-6000-0888-0000-0123-45 ", "TR100004600008880000012345")]
    public void Valid_turkish_ibans_are_normalized(string input, string expected)
    {
        Iban.TryNormalize(input, out var iban).ShouldBeTrue();
        iban.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TR410006200000000000000001")] // kontrol hanesi yanlış
    [InlineData("TR40000620000000000000000")] // 25 karakter
    [InlineData("DE89370400440532013000")] // Türkiye dışı
    [InlineData("TR40000620000000000000000A")]
    public void Invalid_ibans_are_rejected(string? input) => Iban.TryNormalize(input, out _).ShouldBeFalse();

    [Fact]
    public void Iban_is_formatted_in_groups_of_four() =>
        Iban.Format("TR400006200000000000000001").ShouldBe("TR40 0006 2000 0000 0000 0000 01");

    [Theory]
    [InlineData("0.01", null)]
    [InlineData("1250", null)]
    [InlineData("1250.5", null)]
    [InlineData("999999999999.99", null)]
    [InlineData("0", Money.PositiveMessage)]
    [InlineData("-5", Money.PositiveMessage)]
    [InlineData("10.001", Money.DecimalsMessage)]
    [InlineData("1000000000000", Money.MaxMessage)]
    public void Money_rules(string value, string? expected) =>
        Money.Validate(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);

    // Tutar biçimi (karar 2026-10-22); aynı örnekler frontend src/lib/format.test.ts'te. Boşluk bölünmeyen boşluk (U+00A0).
    [Theory]
    [InlineData("1299", "1.299\u00A0TL")]
    [InlineData("1250.5", "1.250,50\u00A0TL")]
    [InlineData("699", "699\u00A0TL")]
    [InlineData("0", "0\u00A0TL")]
    [InlineData("0.5", "0,50\u00A0TL")]
    [InlineData("12990", "12.990\u00A0TL")]
    [InlineData("1234567.89", "1.234.567,89\u00A0TL")]
    [InlineData("-300.5", "-300,50\u00A0TL")]
    [InlineData("100.004", "100\u00A0TL")]
    [InlineData("0.005", "0,01\u00A0TL")]
    [InlineData("-0.001", "0\u00A0TL")]
    public void Money_format(string value, string expected) =>
        Money.Format(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);

    [Fact]
    public void Money_is_required() => Money.Validate(null).ShouldBe(Money.RequiredMessage);
}
