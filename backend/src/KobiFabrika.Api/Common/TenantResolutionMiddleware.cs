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
/// İşletme salt okunur moddaysa (süper admin) panel API'sinin yazma istekleri 403 <c>business_read_only</c>; okuma ve Excel çalışır.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    /// <summary>Salt okunur modda da açık yazmalar: oturum, geri bildirim, bildirimi okundu işaretleme, platform yönetimi.</summary>
    private static readonly string[] ReadOnlyExempt = ["/api/auth/", "/api/feedback", "/api/notifications", "/api/admin/"];

    public async Task InvokeAsync(HttpContext context, TenantContext tenant, CurrentMember member, AppDbContext db, IProblemDetailsService problems)
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
                        .Select(m => new { m.Role, m.Business!.SetupCompletedAt, m.Business.IsReadOnly })
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
                    if (membership.IsReadOnly && IsBlockedWrite(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await problems.WriteAsync(new ProblemDetailsContext
                        {
                            HttpContext = context,
                            ProblemDetails =
                            {
                                Status = StatusCodes.Status403Forbidden,
                                Detail = "İşletme hesabı salt okunur modda. Kayıtları görebilir ve Excel'e aktarabilirsiniz; değişiklik için KobiFabrika destekle iletişime geçin.",
                                Extensions = { ["code"] = "business_read_only" },
                            },
                        });
                        return;
                    }
                }
                else
                {
                    tenant.SetUser(userId);
                }
            }
        }

        await next(context);
    }

    private static bool IsBlockedWrite(HttpRequest request) =>
        !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        && request.Path.StartsWithSegments("/api")
        && !request.Path.StartsWithSegments("/api/public")
        && !ReadOnlyExempt.Any(p => request.Path.Value!.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
