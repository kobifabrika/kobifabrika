namespace KobiFabrika.Domain.Businesses;

/// <summary>Sektörlerin Türkçe adları ve bağlı oldukları şablon (karar günlüğü, Sprint 1).</summary>
public static class SectorCatalog
{
    public sealed record SectorInfo(BusinessSector Sector, string Name, BusinessTemplate Template);

    public static IReadOnlyList<SectorInfo> All { get; } =
    [
        new(BusinessSector.HairBarber, "Kuaför/Berber", BusinessTemplate.Service),
        new(BusinessSector.BeautyCare, "Güzellik ve Bakım", BusinessTemplate.Service),
        new(BusinessSector.ClinicHealth, "Klinik/Sağlık", BusinessTemplate.Service),
        new(BusinessSector.SportsFitness, "Spor ve Fitness", BusinessTemplate.Service),
        new(BusinessSector.AutoService, "Oto Servis", BusinessTemplate.Service),
        new(BusinessSector.RetailStore, "Perakende/Mağaza", BusinessTemplate.Retail),
        new(BusinessSector.CafeRestaurant, "Kafe/Restoran", BusinessTemplate.Retail),
        new(BusinessSector.Other, "Diğer", BusinessTemplate.General),
    ];

    public static BusinessTemplate TemplateFor(BusinessSector sector) =>
        All.First(s => s.Sector == sector).Template;

    /// <summary>Kurulumda (veya Ürünler sayfasından) açılan örnek ürün/hizmet kategorileri.</summary>
    public static IReadOnlyList<string> SampleCategories(BusinessSector sector) => sector switch
    {
        BusinessSector.HairBarber => ["Saç", "Sakal", "Boya ve bakım", "Ürünler"],
        BusinessSector.BeautyCare => ["Cilt bakımı", "Epilasyon", "Manikür ve pedikür", "Ürünler"],
        BusinessSector.ClinicHealth => ["Muayene", "Tedavi", "Kontrol", "Ürünler"],
        BusinessSector.SportsFitness => ["Üyelik", "Özel ders", "Grup dersi", "Ürünler"],
        BusinessSector.AutoService => ["Bakım", "Onarım", "Yedek parça", "Yağ ve sıvılar"],
        BusinessSector.RetailStore => ["Giyim", "Aksesuar", "Ev", "Diğer"],
        BusinessSector.CafeRestaurant => ["Sıcak içecekler", "Soğuk içecekler", "Yiyecekler", "Tatlılar"],
        _ => ["Ürünler", "Hizmetler"],
    };

    /// <summary>Hizmet'te Stok, Perakende'de Randevular varsayılan kapalı; Genel'de hepsi açık.</summary>
    public static BusinessModules DefaultModulesFor(BusinessTemplate template) => template switch
    {
        BusinessTemplate.Service => BusinessModules.Appointments,
        BusinessTemplate.Retail => BusinessModules.Stock,
        _ => BusinessModules.All,
    };
}
