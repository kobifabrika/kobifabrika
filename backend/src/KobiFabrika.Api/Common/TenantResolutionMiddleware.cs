using System.Security.Claims;
using KobiFabrika.Infrastructure.Tenancy;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Oturum açmış kullanıcının aktif işletmesini claim'den okuyup <see cref="TenantContext"/>'e yazar.
/// Oturum yoksa bağlam boş kalır; kiracı filtresi bu durumda hiçbir satır döndürmez.
/// Herkese açık endpoint'ler kiracıyı slug veya imzalı token'dan kendileri çözer.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var hasUserId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            if (Guid.TryParse(user.FindFirstValue(TenantClaimTypes.BusinessId), out var businessId))
            {
                tenant.Set(businessId, hasUserId ? userId : null);
            }
            else if (hasUserId)
            {
                tenant.SetUser(userId);
            }
        }

        return next(context);
    }
}
