using System.Text.Json;

namespace KobiFabrika.Infrastructure.Locations;

/// <summary>
/// Türkiye'nin 81 ili ve ilçeleri (gömülü tr-locations.json; kaynak ve lisans: tr-locations.LICENSE.txt).
/// Hem arayüzdeki seçim listeleri hem sunucu doğrulaması bu listeyi kullanır.
/// </summary>
public sealed class LocationCatalog
{
    public sealed record City(string Code, string Name, IReadOnlyList<string> Districts);

    public IReadOnlyList<City> Cities { get; }

    public LocationCatalog()
    {
        using var stream = typeof(LocationCatalog).Assembly.GetManifestResourceStream("tr-locations.json")
            ?? throw new InvalidOperationException("tr-locations.json gömülü kaynağı bulunamadı.");
        Cities = JsonSerializer.Deserialize<List<City>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public bool IsValid(string? city, string? district) =>
        Cities.FirstOrDefault(c => c.Name == city) is { } match && district is not null && match.Districts.Contains(district);
}
