namespace KobiFabrika.Infrastructure;

/// <summary>Uygulama geneli ayarlar ("App" bölümü).</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>E-postadaki linklerin kökü, ör. http://localhost:3000 veya https://app.kobifabrika.com.</summary>
    public string FrontendUrl { get; set; } = "http://localhost:3000";

    public string Link(string pathAndQuery) => FrontendUrl.TrimEnd('/') + pathAndQuery;
}
