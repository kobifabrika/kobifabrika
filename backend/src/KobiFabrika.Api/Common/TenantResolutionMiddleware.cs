using System.Security.Claims;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Oturum açmış kullanıcının aktif işletmesini claim'den okur ve üyeliği veritabanında doğrular:
/// üyelik aktifse <see cref="TenantContext"/> ve <see cref="CurrentMember"/> doldurulur. Üyelik yok/pasifse
/// kiracı bağlamı boş kalır (fail-closed) ve panel policy'leri 403 döner. Rol cookie'den değil, buradan gelir.
/// Herkese açık endpoint'ler kiracıyı slug veya token'dan kendileri çözer.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenant, CurrentMember member, AppDbContext db)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            Guid? businessId = Guid.TryParse(user.FindFirstValue(TenantClaimTypes.BusinessId), out var id) ? id : null;

            var state = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.EmailConfirmed,
                    Membership = db.BusinessMemberships
                        .Where(m => m.UserId == userId && m.BusinessId == businessId && m.IsActive)
                        .Select(m => new { m.Role, m.Business!.SetupCompletedAt })
                        .FirstOrDefault(),
                })
                .FirstOrDefaultAsync(context.RequestAborted);

            if (state is not null)
            {
                member.SetUser(userId, state.EmailConfirmed);
                if (state.Membership is { } membership && businessId is { } activeBusinessId)
                {
                    member.SetMembership(activeBusinessId, membership.Role, membership.SetupCompletedAt is not null);
                    tenant.Set(activeBusinessId, userId);
                }
                else
                {
                    tenant.SetUser(userId);
                }
            }
        }

        await next(context);
    }
}
