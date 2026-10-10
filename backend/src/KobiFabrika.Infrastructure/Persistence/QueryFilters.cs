namespace KobiFabrika.Infrastructure.Persistence;

/// <summary>
/// Adlı global query filter'lar. Ayrı adlar sayesinde biri kapatılırken diğeri açık kalır:
/// ör. çöp kutusu ekranı <c>IgnoreQueryFilters([QueryFilters.SoftDelete])</c> ile silinmişleri görür
/// ama kiracı filtresi yine çalışır. <see cref="Tenant"/> filtresi sadece süper admin
/// endpoint'lerinde, açıkça kapatılır.
/// </summary>
public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}
