using KobiFabrika.Domain.Common;

namespace KobiFabrika.Tests.Domain;

public sealed class SlugGeneratorTests
{
    [Theory]
    [InlineData("Şık Kuaför & Güzellik", "sik-kuafor-guzellik")]
    [InlineData("IŞIK Oto Servis", "isik-oto-servis")]
    [InlineData("İstanbul Çiçekçilik", "istanbul-cicekcilik")]
    [InlineData("Ağaç Ev Kafe", "agac-ev-kafe")]
    [InlineData("  Öz-Üretim   Ltd. Şti. ", "oz-uretim-ltd-sti")]
    [InlineData("Café Rêve", "cafe-reve")]
    [InlineData("Berber 34", "berber-34")]
    public void Converts_turkish_characters_and_separators(string name, string expected) =>
        SlugGenerator.Generate(name).ShouldBe(expected);

    [Theory]
    [InlineData("Panel", "panel-isletme")]
    [InlineData("API", "api-isletme")]
    [InlineData("Ak", "ak-isletme")]
    [InlineData("!!!", "isletme")]
    public void Avoids_reserved_and_too_short_slugs(string name, string expected) =>
        SlugGenerator.Generate(name).ShouldBe(expected);

    [Fact]
    public void Truncates_to_max_length_without_trailing_dash()
    {
        var slug = SlugGenerator.Generate(new string('a', 58) + " bb cc");

        slug.Length.ShouldBeLessThanOrEqualTo(SlugGenerator.MaxLength);
        slug.ShouldNotEndWith("-");
        SlugGenerator.IsValid(slug).ShouldBeTrue();
    }

    [Fact]
    public void Suffix_keeps_length_limit()
    {
        SlugGenerator.WithSuffix("sik-kuafor", 2).ShouldBe("sik-kuafor-2");
        SlugGenerator.WithSuffix(new string('a', 60), 12).ShouldBe(new string('a', 57) + "-12");
    }

    [Theory]
    [InlineData("sik-kuafor", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("Sik-Kuafor", false)]
    [InlineData("sik--kuafor", false)]
    [InlineData("-sik", false)]
    [InlineData("sik-", false)]
    [InlineData("şık", false)]
    [InlineData("panel", false)]
    [InlineData(null, false)]
    public void Validates_manual_slugs(string? slug, bool valid) =>
        SlugGenerator.IsValid(slug).ShouldBe(valid);
}
