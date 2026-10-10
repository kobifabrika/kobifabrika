namespace KobiFabrika.Domain.Common;

/// <summary>
/// BusinessId alanı olduğu halde bilerek kiracı filtresine girmeyen entity'leri işaretler.
/// Gerekçe yazmak zorunludur; testler BusinessId'li ama ITenantScoped olmayan
/// her entity'de bu işareti arar.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantExemptAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
