namespace KobiFabrika.Domain.Businesses;

/// <summary>
/// Kurulum sihirbazında seçilen sektör. Her sektör bir <see cref="BusinessTemplate"/>'e bağlıdır
/// (<see cref="SectorCatalog"/>); şablon menüyü ve varsayılan ayarları belirler.
/// </summary>
public enum BusinessSector
{
    HairBarber,
    BeautyCare,
    ClinicHealth,
    SportsFitness,
    AutoService,
    RetailStore,
    CafeRestaurant,
    Other,
}
