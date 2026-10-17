namespace KobiFabrika.Infrastructure;

/// <summary>Uygulama geneli ayarlar ("App" bölümü).</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>E-postadaki linklerin kökü, ör. http://localhost:3000 veya https://app.kobifabrika.com.</summary>
    public string FrontendUrl { get; set; } = "http://localhost:3000";

    public string Link(string pathAndQuery) => FrontendUrl.TrimEnd('/') + pathAndQuery;

    /// <summary>API'nin dışarıdan görünen kökü (e-postada logo gibi dosya linkleri): http://localhost:5080, https://api.kobifabrika.com.</summary>
    public string ApiUrl { get; set; } = "http://localhost:5080";

    /// <summary>"/files/..." gibi API'ye göreli yolu mutlak adrese çevirir.</summary>
    public string? ApiLink(string? path) => string.IsNullOrEmpty(path) ? null
        : path.StartsWith("http", StringComparison.Ordinal) ? path : ApiUrl.TrimEnd('/') + path;
}
