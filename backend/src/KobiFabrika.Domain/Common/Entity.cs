namespace KobiFabrika.Domain.Common;

/// <summary>
/// Tüm entity'lerin tabanı. Id, sıralı (v7) Guid olarak istemcide üretilir;
/// böylece clustered index parçalanmaz.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}
