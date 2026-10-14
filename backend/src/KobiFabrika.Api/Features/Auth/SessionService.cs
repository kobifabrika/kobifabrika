using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Auth;

public sealed record MeResponse(MeUser User, MeBusiness? Business, BusinessRole? Role, IReadOnlyList<MeMembership> Memberships);

/// <summary>IsPlatformAdmin: KobiFabrika ekibi (menüde "Kredi yükle (platform)" ekranı).</summary>
public sealed record MeUser(Guid Id, string FullName, string Email, bool EmailConfirmed, string? Phone, bool IsPlatformAdmin);

/// <summary>
/// Phone (E.164), Address, City, District: fiş ve belgelerin başlığı için; ReceiptWidth fiş genişliği (mm);
/// DefaultDueDays: veresiye satışta önerilen vade (her rol görür).
/// </summary>
public sealed record MeBusiness(
    Guid Id,
    string Name,
    string Slug,
    BusinessSector Sector,
    BusinessTemplate Template,
    ModulesDto Modules,
    string? LogoUrl,
    bool SetupCompleted,
    string? Phone,
    string? Address,
    string? City,
    string? District,
    int ReceiptWidth,
    int DefaultDueDays);

public sealed record ModulesDto(bool Appointments, bool Stock)
{
    public static ModulesDto From(BusinessModules modules) =>
        new(modules.HasFlag(BusinessModules.Appointments), modules.HasFlag(BusinessModules.Stock));

    public BusinessModules ToFlags() =>
        (Appointments ? BusinessModules.Appointments : BusinessModules.None) | (Stock ? BusinessModules.Stock : BusinessModules.None);
}

public sealed record MeMembership(Guid BusinessId, string BusinessName, BusinessRole Role);

/// <summary>
/// Oturum açma ve aktif işletme seçimi. Aktif işletme <see cref="AppUser.LastBusinessId"/>'de tutulur ve
/// cookie'ye claim olarak yazılır (AppClaimsPrincipalFactory). Rol her istekte üyelikten okunur.
/// </summary>
public sealed class SessionService(
    AppDbContext db,
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    PlatformAdmins admins)
{
    /// <summary>Kullanıcının aktif üyelikleri; en son seçilen önce, sonra en eski üyelik.</summary>
    private Task<List<MeMembership>> ActiveMembershipsAsync(Guid userId, CancellationToken ct) =>
        db.BusinessMemberships
            .Where(m => m.UserId == userId && m.IsActive)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MeMembership(m.BusinessId, m.Business!.Name, m.Role))
            .ToListAsync(ct);

    /// <summary>LastBusinessId hâlâ aktif bir üyeliği gösteriyorsa onu, yoksa en eski aktif üyeliği seçer ve kaydeder.</summary>
    public async Task<Guid?> EnsureActiveBusinessAsync(AppUser user, CancellationToken ct)
    {
        var memberships = await ActiveMembershipsAsync(user.Id, ct);
        var chosen = memberships.FirstOrDefault(m => m.BusinessId == user.LastBusinessId)?.BusinessId
            ?? memberships.FirstOrDefault()?.BusinessId;

        if (chosen != user.LastBusinessId)
        {
            user.LastBusinessId = chosen;
            await users.UpdateAsync(user);
        }

        return chosen;
    }

    public async Task SignInAsync(AppUser user, bool persistent, CancellationToken ct)
    {
        await EnsureActiveBusinessAsync(user, ct);
        await signIn.SignInAsync(user, persistent);
    }

    /// <summary>Aktif işletmeyi değiştirir ve cookie'yi yeniler (kalıcılık korunur).</summary>
    public async Task SwitchBusinessAsync(AppUser user, Guid businessId)
    {
        user.LastBusinessId = businessId;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);
    }

    public async Task<MeResponse> BuildMeAsync(AppUser user, Guid? businessId, CancellationToken ct)
    {
        var memberships = await ActiveMembershipsAsync(user.Id, ct);
        var current = memberships.FirstOrDefault(m => m.BusinessId == businessId);

        MeBusiness? business = null;
        if (current is not null)
        {
            business = await db.Businesses
                .Where(b => b.Id == current.BusinessId)
                .Select(b => new MeBusiness(
                    b.Id, b.Name, b.Slug, b.Sector, b.Template, ModulesDto.From(b.Modules), b.LogoUrl, b.SetupCompletedAt != null,
                    b.Phone, b.Address, b.City, b.District, b.ReceiptWidth, b.DefaultDueDays))
                .FirstAsync(ct);
        }

        return new MeResponse(
            new MeUser(user.Id, user.FullName, user.Email!, user.EmailConfirmed, user.PhoneNumber, await admins.IsAdminAsync(user.Id)),
            business,
            current?.Role,
            memberships);
    }

    public static Guid? ActiveBusinessOf(CurrentMember member) => member.IsMember ? member.BusinessId : null;
}
