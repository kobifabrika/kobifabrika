namespace KobiFabrika.Domain.Common;

/// <summary>
/// Bir işletmeye (kiracıya) ait kayıt. Bu arayüzü uygulayan her entity
/// AppDbContext'te kiracı filtresine girer ve BusinessId'si SaveChanges'te otomatik atanır.
/// </summary>
public interface ITenantScoped
{
    Guid BusinessId { get; set; }
}
