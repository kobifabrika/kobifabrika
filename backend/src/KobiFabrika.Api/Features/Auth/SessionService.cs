using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Users;
using KobiFabrika.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Auth;

public sealed record MeResponse(
    MeUser User, MeBusiness? Business, BusinessRole? Role, IReadOnlyList<MeMembership> Memberships, MePlatform Platform);

/// <summary>Platform ayarları: SMS kapalıyken SMS ekranları gizlenir, WhatsApp/e-posta akışları görünür.</summary>
public sealed record MePlatform(bool SmsEnabled);

/// <summary>
/// Etkin paket. Features: açık özellikler (campaigns, automations, onlineBooking, commissions, surveys, riskList).
/// Limitlerde null = sınırsız. RequestedPlan/RequestedPeriod: işletmenin seçtiği paket (ödeme yok).
/// </summary>
public sealed record MePlan(
    PlanStatus Status,
    string Code,
    string Name,
    DateTime? TrialEndsAt,
    bool IsReadOnly,
    IReadOnlyList<string> Features,
    int? MaxUsers,
    int? MaxContacts,
    int? MaxAppointmentsPerMonth,
    string? RequestedPlan,
    string? RequestedPeriod)
{
    public static IReadOnlyList<string> FeatureKeys(PlanFeatures features) =>
        Enum.GetValues<PlanFeatures>()
            .Where(f => f is not PlanFeatures.None and not PlanFeatures.All && features.HasFlag(f))
            .Select(f => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(f.ToString()))
            .ToList();
}

/// <summary>
/// IsPlatformAdmin: KobiFabrika ekibi (süper admin, /platform). ConsentRequired: işletme Sahibi güncel Kullanım koşulları ve Veri
/// işleme sözleşmesini henüz onaylamadı (panel bir kez onay ister).
/// </summary>
public sealed record MeUser(
    Guid Id, string FullName, string Email, bool EmailConfirmed, string? Phone, bool IsPlatformAdmin, bool ConsentRequired);

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
    int DefaultDueDays,
    MePlan Plan);

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
    PlatformAdmins admins,
    TimeProvider clock,
    IOptionsMonitor<MessagingOptions> messaging)
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
            var b = await db.Businesses.AsNoTracking().FirstAsync(x => x.Id == current.BusinessId, ct);
            var plans = await db.Plans.AsNoTracking().ToListAsync(ct);
            var effective = EffectivePlan.Resolve(plans.Single(p => p.Code == b.PlanCode), plans.Single(p => p.Code == PlanCodes.Usta),
                b.IsPilot, b.TrialEndsAt, b.IsReadOnly, clock.GetUtcNow().UtcDateTime);
            var plan = new MePlan(effective.Status, effective.PlanCode, effective.PlanName, effective.TrialEndsAt, effective.IsReadOnly,
                MePlan.FeatureKeys(effective.Features), effective.MaxUsers, effective.MaxContacts, effective.MaxAppointmentsPerMonth,
                b.RequestedPlanCode, b.RequestedPeriod);
            business = new MeBusiness(
                b.Id, b.Name, b.Slug, b.Sector, b.Template, ModulesDto.From(b.Modules), b.LogoUrl, b.SetupCompletedAt != null,
                b.Phone, b.Address, b.City, b.District, b.ReceiptWidth, b.DefaultDueDays, plan);
        }

        var consentRequired = current?.Role == BusinessRole.Owner && await ConsentMissingAsync(user.Id, ct);
        return new MeResponse(
            new MeUser(user.Id, user.FullName, user.Email!, user.EmailConfirmed, user.PhoneNumber, await admins.IsAdminAsync(user.Id),
                consentRequired),
            business,
            current?.Role,
            memberships,
            new MePlatform(messaging.CurrentValue.SmsEnabled));
    }

    private async Task<bool> ConsentMissingAsync(Guid userId, CancellationToken ct)
    {
        var accepted = await db.UserConsents.AsNoTracking()
            .Where(c => c.UserId == userId
                && ((c.Kind == UserConsentKind.TermsOfUse && c.Version == LegalVersions.TermsOfUse)
                    || (c.Kind == UserConsentKind.DataProcessingAgreement && c.Version == LegalVersions.DataProcessingAgreement)))
            .Select(c => c.Kind).Distinct().CountAsync(ct);
        return accepted < 2;
    }

    public static Guid? ActiveBusinessOf(CurrentMember member) => member.IsMember ? member.BusinessId : null;
}
