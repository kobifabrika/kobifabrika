using System.Security.Claims;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Infrastructure.Identity;

/// <summary>
/// Oturum cookie'sine aktif işletmeyi (<see cref="AppUser.LastBusinessId"/>) yazar. Rol cookie'de tutulmaz;
/// her istekte üyelikten okunur (TenantResolutionMiddleware). Security stamp yenilemesinde de aynı
/// factory çalıştığı için işletme claim'i kaybolmaz.
/// </summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("name", user.FullName));
        if (user.LastBusinessId is { } businessId)
        {
            identity.AddClaim(new Claim(TenantClaimTypes.BusinessId, businessId.ToString()));
        }

        return identity;
    }
}
