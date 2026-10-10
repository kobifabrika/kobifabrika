namespace KobiFabrika.Domain.Businesses;

/// <summary>
/// Şablona göre açılıp kapanabilen menü modülleri. Diğer modüller her işletmede açıktır.
/// Ayarlar'dan değiştirilebilir; veritabanında int olarak saklanır.
/// </summary>
[Flags]
public enum BusinessModules
{
    None = 0,
    Appointments = 1,
    Stock = 2,
    All = Appointments | Stock,
}
