using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Billing;

/// <summary>Pakete bağlı özellikler. Kapalı özellik salt okunur görünür; yazma uçları 403 <c>plan_feature</c>.</summary>
[Flags]
public enum PlanFeatures
{
    None = 0,
    Campaigns = 1,
    Automations = 2,
    OnlineBooking = 4,
    Commissions = 8,
    Surveys = 16,
    RiskList = 32,
    All = Campaigns | Automations | OnlineBooking | Commissions | Surveys | RiskList,
}

public static class PlanCodes
{
    public const string Cirak = "cirak";
    public const string Kalfa = "kalfa";
    public const string Usta = "usta";

    public static readonly string[] All = [Cirak, Kalfa, Usta];

    public static bool IsValid(string? code) => code is not null && All.Contains(code);
}

public static class PlanPeriods
{
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";

    public static bool IsValid(string? period) => period is Monthly or Yearly;
}

/// <summary>
/// Paket (global, kiracı tablosu değil). Fiyat ve limitler veritabanından gelir, süper adminden değişir.
/// Limitlerde null = sınırsız. Fiyatlar TL, KDV hariç.
/// </summary>
public class Plan : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }

    /// <summary>Giriş yapan kullanıcılar (aktif üyeler + bekleyen davetler); Mali Müşavir sayılmaz.</summary>
    public int? MaxUsers { get; set; }

    /// <summary>Müşteri işaretli, silinmemiş kayıtlar (arşiv dahil, tedarikçi hariç).</summary>
    public int? MaxContacts { get; set; }

    /// <summary>Randevu tarihi o İstanbul ayında olan, iptal edilmemiş randevular.</summary>
    public int? MaxAppointmentsPerMonth { get; set; }

    public PlanFeatures Features { get; set; }
    public int SortOrder { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>İşletmenin şu anki paket durumu.</summary>
public enum PlanStatus
{
    /// <summary>Deneme sürüyor: bütün özellikler açık, Usta limitleri.</summary>
    Trial,

    /// <summary>Pilot işletme: süresiz, limitsiz, bütün özellikler.</summary>
    Pilot,

    /// <summary>Paketin kendisi (Çırak, Kalfa veya Usta).</summary>
    Plan,
}

/// <summary>Etkin paket: özellikler ve limitler. Saf hesap; Çırak'a düşmek için iş gerekmez, saatten hesaplanır.</summary>
public sealed record EffectivePlan(
    PlanStatus Status,
    string PlanCode,
    string PlanName,
    PlanFeatures Features,
    int? MaxUsers,
    int? MaxContacts,
    int? MaxAppointmentsPerMonth,
    DateTime? TrialEndsAt,
    bool IsReadOnly)
{
    public bool Has(PlanFeatures feature) => (Features & feature) == feature;

    /// <param name="plan">İşletmenin paketi (deneme bitince geçerli olan).</param>
    /// <param name="trialPlan">Deneme süresince limitleri kullanılan paket (Usta).</param>
    public static EffectivePlan Resolve(
        Plan plan, Plan trialPlan, bool isPilot, DateTime? trialEndsAt, bool isReadOnly, DateTime nowUtc)
    {
        if (isPilot)
        {
            return new EffectivePlan(PlanStatus.Pilot, plan.Code, plan.Name, PlanFeatures.All, null, null, null, null, isReadOnly);
        }

        if (trialEndsAt is { } end && nowUtc < end)
        {
            return new EffectivePlan(PlanStatus.Trial, plan.Code, plan.Name, PlanFeatures.All,
                trialPlan.MaxUsers, trialPlan.MaxContacts, trialPlan.MaxAppointmentsPerMonth, end, isReadOnly);
        }

        return new EffectivePlan(PlanStatus.Plan, plan.Code, plan.Name, plan.Features,
            plan.MaxUsers, plan.MaxContacts, plan.MaxAppointmentsPerMonth, trialEndsAt, isReadOnly);
    }

    /// <summary>Özelliği açan ilk paket (sıraya göre); yoksa null.</summary>
    public static Plan? UnlockingPlan(IEnumerable<Plan> plans, PlanFeatures feature) =>
        plans.OrderBy(p => p.SortOrder).FirstOrDefault(p => (p.Features & feature) == feature);

    public const int TrialDays = 14;
}
