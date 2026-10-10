using Microsoft.AspNetCore.Identity;

namespace KobiFabrika.Infrastructure.Identity;

/// <summary>
/// Platform düzeyindeki roller (ör. PlatformAdmin). İşletme içi roller
/// (Owner/Manager/Staff/Accountant) BusinessMembership.Role'de tutulur.
/// </summary>
public class AppRole : IdentityRole<Guid>
{
    public AppRole() => Id = Guid.CreateVersion7();

    public const string PlatformAdmin = "PlatformAdmin";
}
