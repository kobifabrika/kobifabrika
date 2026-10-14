using KobiFabrika.Domain.Marketing;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// "PublicLinks" bölümü: SMS'teki kısa linklerin kökü (çıkış /c/{kod}; 6B'de anket). Geliştirmede http://localhost:3000;
/// canlıda alan adı alınınca "kobifabrika.com". Değer SMS'e aynen yazılır; segment sayısı bu uzunlukla hesaplanır.
/// </summary>
public sealed class PublicLinkOptions
{
    public const string Section = "PublicLinks";

    public string ShortBaseUrl { get; set; } = "http://localhost:3000";
}

/// <summary>"Marketing" bölümü: gönderim aralığı, çıkış anahtar kelimesi, parça boyu.</summary>
public sealed class MarketingOptions
{
    public const string Section = "Marketing";

    /// <summary>Pazarlama SMS'i gönderim aralığı (İstanbul, "HH:mm"); E2E'de 00:00-24:00 (CI gece de koşar).</summary>
    public string SendWindowStart { get; set; } = "09:00";

    public string SendWindowEnd { get; set; } = "21:00";

    /// <summary>Çıkış ekindeki ret metni; gerçek sağlayıcı gelince "RET yazip 3XXX'e gonderin" olur (ASCII kalmalı).</summary>
    public string OptOutKeyword { get; set; } = "RET";

    /// <summary>Kampanya gönderiminde bir parçadaki alıcı sayısı.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>"Gönderiliyor"da bu kadar dakikadan uzun kalan alıcı yeniden gönderilmez, "Başarısız (belirsiz)" olur.</summary>
    public int StaleMinutes { get; set; } = 10;

    public MarketingWindow Window => new(Parse(SendWindowStart), Parse(SendWindowEnd));

    private static int Parse(string hhmm)
    {
        var parts = hhmm.Split(':');
        return int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) * 60
            + (parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0);
    }
}
