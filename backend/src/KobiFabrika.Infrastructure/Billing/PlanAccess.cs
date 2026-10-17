using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Billing;

/// <summary>Limit kontrolünün sonucu. <see cref="Allowed"/> false ise <see cref="Message"/> kullanıcıya gösterilir.</summary>
public sealed record PlanCheck(bool Allowed, string? Message = null, int? Limit = null, int? Used = null)
{
    public static readonly PlanCheck Ok = new(true);
}

/// <summary>
/// Etkin paket, özellikler ve limitler (karar günlüğü 2026-10-17). İstek (scope) başına bir kez yüklenir.
/// Kiracı bağlamı olan scope'ta kullanılır; işletme ve paketler kiracı filtresi dışındadır.
/// </summary>
public interface IPlanAccess
{
    Task<EffectivePlan> GetAsync(CancellationToken ct = default);

    /// <summary>Özellik kapalıysa kullanıcıya gösterilecek mesaj ("Kampanyalar Kalfa paketiyle açılır."); açıksa null.</summary>
    Task<string?> FeatureBlockedMessageAsync(PlanFeatures feature, CancellationToken ct = default);

    Task<PlanCheck> CheckUserAsync(BusinessRole role, CancellationToken ct = default);

    Task<PlanCheck> CheckContactsAsync(int count, CancellationToken ct = default);

    /// <summary>Randevu tarihi o İstanbul ayındaki iptal edilmemiş randevu sayısına göre (randevunun kendisi hariç).</summary>
    Task<PlanCheck> CheckAppointmentAsync(DateTime startUtc, Guid appointmentId, CancellationToken ct = default);

    /// <summary>Kullanıcı sınırına sayılanlar: aktif üyeler + bekleyen davetler; Mali Müşavir hariç.</summary>
    Task<int> CountBillableUsersAsync(CancellationToken ct = default);

    Task<int> CountContactsAsync(CancellationToken ct = default);

    Task<int> CountAppointmentsInMonthAsync(DateOnly anyDayInMonth, Guid? excludeId, CancellationToken ct = default);
}

internal sealed class PlanAccess(AppDbContext db, TimeProvider clock) : IPlanAccess
{
    private EffectivePlan? _plan;
    private List<Plan>? _plans;

    public async Task<EffectivePlan> GetAsync(CancellationToken ct = default)
    {
        if (_plan is not null)
        {
            return _plan;
        }

        var businessId = db.CurrentBusinessId ?? throw new InvalidOperationException("Kiracı bağlamı olmadan paket okunamaz.");
        var business = await db.Businesses.AsNoTracking().IgnoreQueryFilters()
            .Where(b => b.Id == businessId)
            .Select(b => new { b.PlanCode, b.IsPilot, b.TrialEndsAt, b.IsReadOnly })
            .SingleAsync(ct);
        var plans = await PlansAsync(ct);
        var plan = plans.Single(p => p.Code == business.PlanCode);
        var trialPlan = plans.Single(p => p.Code == PlanCodes.Usta);
        _plan = EffectivePlan.Resolve(plan, trialPlan, business.IsPilot, business.TrialEndsAt, business.IsReadOnly,
            clock.GetUtcNow().UtcDateTime);
        return _plan;
    }

    private async Task<List<Plan>> PlansAsync(CancellationToken ct) =>
        _plans ??= await db.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);

    public async Task<string?> FeatureBlockedMessageAsync(PlanFeatures feature, CancellationToken ct = default)
    {
        if ((await GetAsync(ct)).Has(feature))
        {
            return null;
        }

        var unlocking = EffectivePlan.UnlockingPlan(await PlansAsync(ct), feature);
        var name = FeatureName(feature);
        return unlocking is null
            ? $"{name} paketinizde açık değil."
            : $"{name} {unlocking.Name} paketiyle açılır. Mevcut kayıtlarınızı görmeye devam edebilirsiniz.";
    }

    public static string FeatureName(PlanFeatures feature) => feature switch
    {
        PlanFeatures.Campaigns => "Kampanyalar",
        PlanFeatures.Automations => "Otomasyonlar",
        PlanFeatures.OnlineBooking => "Online randevu",
        PlanFeatures.Commissions => "Prim",
        PlanFeatures.Surveys => "Memnuniyet anketi",
        PlanFeatures.RiskList => "Kaybedilme riski listesi",
        _ => "Bu özellik",
    };

    public async Task<PlanCheck> CheckUserAsync(BusinessRole role, CancellationToken ct = default)
    {
        var plan = await GetAsync(ct);
        if (role == BusinessRole.Accountant || plan.MaxUsers is not { } max)
        {
            return PlanCheck.Ok;
        }

        var used = await CountBillableUsersAsync(ct);
        return used < max
            ? PlanCheck.Ok
            : new PlanCheck(false,
                $"{plan.PlanName} paketinde en fazla {max} kullanıcı olabilir (bekleyen davetler dahil). Mali müşavir sınıra sayılmaz.",
                max, used);
    }

    public async Task<PlanCheck> CheckContactsAsync(int count, CancellationToken ct = default)
    {
        var plan = await GetAsync(ct);
        if (plan.MaxContacts is not { } max || count <= 0)
        {
            return PlanCheck.Ok;
        }

        var used = await CountContactsAsync(ct);
        if (used + count <= max)
        {
            return PlanCheck.Ok;
        }

        var message = count == 1
            ? $"{plan.PlanName} paketinde en fazla {Format(max)} müşteri kaydı olabilir; şu an {Format(used)} kayıt var."
            : $"{plan.PlanName} paketinde en fazla {Format(max)} müşteri kaydı olabilir; şu an {Format(used)} kayıt var, {Format(Math.Max(0, max - used))} kayıt daha eklenebilir.";
        return new PlanCheck(false, message, max, used);
    }

    public async Task<PlanCheck> CheckAppointmentAsync(DateTime startUtc, Guid appointmentId, CancellationToken ct = default)
    {
        var plan = await GetAsync(ct);
        if (plan.MaxAppointmentsPerMonth is not { } max)
        {
            return PlanCheck.Ok;
        }

        var used = await CountAppointmentsInMonthAsync(BusinessTime.LocalDay(startUtc), appointmentId, ct);
        return used < max
            ? PlanCheck.Ok
            : new PlanCheck(false,
                $"{plan.PlanName} paketinde ayda en fazla {Format(max)} randevu verilebilir; bu ay {Format(used)} randevu var. İptal edilen randevular sayılmaz.",
                max, used);
    }

    public async Task<int> CountBillableUsersAsync(CancellationToken ct = default)
    {
        var businessId = db.CurrentBusinessId;
        var now = clock.GetUtcNow().UtcDateTime;

        var members = await db.BusinessMemberships
            .CountAsync(m => m.BusinessId == businessId && m.IsActive && m.Role != BusinessRole.Accountant, ct);
        var pending = await db.Invitations
            .CountAsync(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now && i.Role != BusinessRole.Accountant, ct);

        return members + pending;
    }

    /// <summary>Müşteri işaretli, silinmemiş kayıtlar (arşiv dahil; sadece tedarikçi olanlar hariç).</summary>
    public Task<int> CountContactsAsync(CancellationToken ct = default) =>
        db.Contacts.CountAsync(c => c.IsCustomer, ct);

    public Task<int> CountAppointmentsInMonthAsync(DateOnly anyDayInMonth, Guid? excludeId, CancellationToken ct = default)
    {
        var first = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var (start, end) = BusinessTime.RangeUtc(first, first.AddMonths(1).AddDays(-1));
        return db.Appointments.CountAsync(a => a.StartAt >= start && a.StartAt < end
            && a.Status != AppointmentStatus.Cancelled && a.Id != excludeId, ct);
    }

    private static string Format(int n) => n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
}
