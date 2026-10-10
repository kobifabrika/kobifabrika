namespace KobiFabrika.Domain.Common;

/// <summary>Silindiğinde fiziksel olarak silinmez, IsDeleted = true olur.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
