namespace KobiFabrika.Infrastructure.Tenancy;

/// <summary>
/// İsteğin (veya arka plan işinin) hangi işletme ve kullanıcı adına çalıştığı.
/// BusinessId null ise kiracı filtresi hiçbir satır döndürmez (fail-closed).
/// </summary>
public interface ITenantContext
{
    Guid? BusinessId { get; }
    Guid? UserId { get; }
}
