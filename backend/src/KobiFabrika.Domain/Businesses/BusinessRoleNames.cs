namespace KobiFabrika.Domain.Businesses;

public static class BusinessRoleNames
{
    /// <summary>Rolün arayüzde ve e-postalarda görünen Türkçe adı.</summary>
    public static string DisplayName(this BusinessRole role) => role switch
    {
        BusinessRole.Owner => "Sahip",
        BusinessRole.Manager => "Yönetici",
        BusinessRole.Staff => "Personel",
        BusinessRole.Accountant => "Mali Müşavir",
        _ => role.ToString(),
    };
}
