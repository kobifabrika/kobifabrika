namespace KobiFabrika.Domain.Catalog;

/// <summary>
/// Yasal KDV oranları (tüm işletmeler için ortak, işletmeye ait değil). Migration'da %0, %1, %10, %20 eklenir;
/// oran değişirse yeni satır eklenir, eskisi pasife alınır (eski ürünler ve satışlar oranı korur).
/// </summary>
public class VatRate
{
    /// <summary>Oran (yüzde), birincil anahtar: 0, 1, 10, 20.</summary>
    public decimal Rate { get; set; }

    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public static readonly decimal[] Defaults = [0m, 1m, 10m, 20m];
}
