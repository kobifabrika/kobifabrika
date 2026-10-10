using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KobiFabrika.Domain.Common;

/// <summary>
/// İşletme adından URL parçası (slug) üretir: kobifabrika.com/r/{slug}.
/// Türkçe karakterler dönüştürülür (ş→s, ğ→g, ı/İ→i, ö→o, ü→u, ç→c), harf/rakam dışı her şey tire olur.
/// Benzersizlik veritabanında kontrol edilir (Infrastructure).
/// </summary>
public static partial class SlugGenerator
{
    public const int MinLength = 3;
    public const int MaxLength = 60;

    /// <summary>Sistem yollarıyla çakışan veya yanıltıcı olabilecek adlar.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "api", "app", "panel", "giris", "kayit", "kurulum", "davet", "destek", "yardim",
        "kobifabrika", "www", "mail", "static", "files", "r", "sifremi-unuttum", "sifre-sifirla",
        "e-posta-dogrula", "ayarlar", "test",
    };

    private static readonly Dictionary<char, string> TurkishMap = new()
    {
        ['ş'] = "s", ['Ş'] = "s", ['ğ'] = "g", ['Ğ'] = "g", ['ı'] = "i", ['I'] = "i", ['İ'] = "i",
        ['ö'] = "o", ['Ö'] = "o", ['ü'] = "u", ['Ü'] = "u", ['ç'] = "c", ['Ç'] = "c",
    };

    /// <summary>Addan slug üretir. Sonuç her zaman <see cref="IsValid"/>'dir.</summary>
    public static string Generate(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            builder.Append(TurkishMap.TryGetValue(ch, out var mapped) ? mapped : ch.ToString());
        }

        // Diğer aksanlı harfler (é, â …) temel harfe indirilir.
        var decomposed = builder.ToString().Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                ascii.Append(char.ToLowerInvariant(ch));
            }
        }

        var slug = NonSlugChars().Replace(ascii.ToString(), "-").Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        if (slug.Length == 0)
        {
            return "isletme";
        }

        if (slug.Length < MinLength || Reserved.Contains(slug))
        {
            slug = $"{slug}-isletme";
        }

        return slug;
    }

    /// <summary>Ayarlar'dan elle girilen slug'ın kurallara uyup uymadığı.</summary>
    public static bool IsValid(string? slug) =>
        slug is { Length: >= MinLength and <= MaxLength }
        && ValidSlug().IsMatch(slug)
        && !slug.Contains("--", StringComparison.Ordinal)
        && !Reserved.Contains(slug);

    /// <summary>Çakışma olduğunda sona "-2", "-3"… ekler; uzunluk sınırını korur.</summary>
    public static string WithSuffix(string slug, int number)
    {
        var suffix = $"-{number}";
        var head = slug.Length + suffix.Length > MaxLength ? slug[..(MaxLength - suffix.Length)].TrimEnd('-') : slug;
        return head + suffix;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex ValidSlug();
}
