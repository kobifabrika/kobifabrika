namespace KobiFabrika.Infrastructure.Tenancy;

/// <summary>
/// Scoped kiracı bağlamı. Panel isteklerinde oturumdaki claim'den, herkese açık
/// endpoint'lerde slug/token çözüldükten sonra, arka plan işlerinde işin başında
/// bir kez <see cref="Set"/> ile doldurulur. Aynı scope içinde başka bir işletmeye geçilemez.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? BusinessId { get; private set; }
    public Guid? UserId { get; private set; }

    public void Set(Guid businessId, Guid? userId = null)
    {
        if (BusinessId is { } current && current != businessId)
        {
            throw new TenantViolationException("Kiracı bağlamı aynı işlem içinde değiştirilemez.");
        }

        BusinessId = businessId;
        UserId = userId ?? UserId;
    }

    /// <summary>Kiracı olmadan, sadece kullanıcı bilinen durumlar için (ör. işletme seçmeden önce).</summary>
    public void SetUser(Guid userId) => UserId = userId;
}
