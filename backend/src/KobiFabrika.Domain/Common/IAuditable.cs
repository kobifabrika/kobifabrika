namespace KobiFabrika.Domain.Common;

/// <summary>Oluşturma/güncelleme bilgisi SaveChanges'te otomatik doldurulur. Zamanlar UTC.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
}
