using FluentValidation;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Platform;

namespace KobiFabrika.Api.Features.Admin;

/// <summary>Süper admin özeti: sadece sayılar (KVKK: işletmelerin müşteri verisi yok).</summary>
public sealed record AdminOverview(
    int Businesses,
    int Trial,
    int Pilot,
    int Cirak,
    int Kalfa,
    int Usta,
    int ReadOnly,
    int NewLast7Days,
    int OpenFeedback,
    IReadOnlyList<AdminBusinessItem> Recent);

/// <summary>
/// İşletme satırı. Status: Trial/Pilot/Plan; PlanCode deneme bitince geçerli paket. Sayılar: kullanıcı (Mali Müşavir hariç,
/// aktif), müşteri, bu ay randevu. LastLoginAt: işletmenin aktif üyelerinin en son girişi.
/// </summary>
public sealed record AdminBusinessItem(
    Guid Id,
    string Name,
    string Slug,
    BusinessSector Sector,
    string? City,
    DateTime CreatedAt,
    bool SetupCompleted,
    PlanStatus Status,
    string PlanCode,
    string PlanName,
    DateTime? TrialEndsAt,
    bool IsReadOnly,
    string? OwnerName,
    string? OwnerEmail,
    DateTime? LastLoginAt,
    int Users,
    int Contacts,
    int AppointmentsThisMonth,
    int SmsBalance);

public sealed record AdminBusinessList(IReadOnlyList<AdminBusinessItem> Items, int Total);

/// <summary>İşletme ayrıntısı: özet + iletişim, istenen paket, toplam sayılar (içerik yok).</summary>
public sealed record AdminBusinessDetail(
    AdminBusinessItem Summary,
    string? Phone,
    string? Email,
    string? District,
    string? ReadOnlyReason,
    string? RequestedPlan,
    string? RequestedPeriod,
    int AppointmentsTotal,
    int SalesTotal,
    int StaffMembers,
    bool BookingEnabled,
    int OpenFeedback);

public sealed record AdminUserItem(Guid UserId, string FullName, string Email, bool EmailConfirmed, string? Phone, BusinessRole Role,
    bool IsActive, DateTime JoinedAt, DateTime? LastLoginAt);

public sealed record AdminNote(Guid Id, string Body, string AuthorName, DateTime CreatedAt);

public sealed record AdminActivityItem(Guid Id, PlatformAction Action, string? Details, string AdminName, DateTime At);

public sealed record AdminPlanRequest(string PlanCode);

public sealed record AdminPilotRequest(bool IsPilot);

public sealed record AdminReadOnlyRequest(bool IsReadOnly, string? Reason);

public sealed record AdminTrialExtendRequest(int Days);

public sealed record AdminNoteRequest(string Body);

/// <summary>Paket (süper admin düzenler). Limitlerde null = sınırsız. Features: campaigns, automations, onlineBooking, commissions, surveys, riskList.</summary>
public sealed record AdminPlanItem(
    string Code, string Name, decimal MonthlyPrice, decimal YearlyPrice, int? MaxUsers, int? MaxContacts, int? MaxAppointmentsPerMonth,
    IReadOnlyList<string> Features, DateTime? UpdatedAt);

public sealed record AdminPlanUpdateRequest(
    decimal MonthlyPrice, decimal YearlyPrice, int? MaxUsers, int? MaxContacts, int? MaxAppointmentsPerMonth, IReadOnlyList<string> Features);

public sealed class AdminNoteRequestValidator : AbstractValidator<AdminNoteRequest>
{
    public AdminNoteRequestValidator() =>
        RuleFor(x => x.Body).NotEmpty().WithMessage("Not yazın.").MaximumLength(SupportNote.MaxBodyLength)
            .WithMessage($"Not en fazla {SupportNote.MaxBodyLength} karakter olabilir.");
}

public sealed class AdminTrialExtendRequestValidator : AbstractValidator<AdminTrialExtendRequest>
{
    public AdminTrialExtendRequestValidator() =>
        RuleFor(x => x.Days).InclusiveBetween(1, 365).WithMessage("Gün 1 ile 365 arasında olmalı.");
}

public sealed class AdminReadOnlyRequestValidator : AbstractValidator<AdminReadOnlyRequest>
{
    public AdminReadOnlyRequestValidator() =>
        RuleFor(x => x.Reason).MaximumLength(500).WithMessage("Gerekçe en fazla 500 karakter olabilir.");
}

public sealed class AdminPlanUpdateRequestValidator : AbstractValidator<AdminPlanUpdateRequest>
{
    public AdminPlanUpdateRequestValidator()
    {
        RuleFor(x => x.MonthlyPrice).InclusiveBetween(0, 1_000_000).WithMessage("Aylık fiyat 0 ile 1.000.000 TL arasında olmalı.");
        RuleFor(x => x.YearlyPrice).InclusiveBetween(0, 10_000_000).WithMessage("Yıllık fiyat 0 ile 10.000.000 TL arasında olmalı.");
        RuleFor(x => x.MaxUsers).InclusiveBetween(1, 1000).When(x => x.MaxUsers is not null).WithMessage("Kullanıcı 1 ile 1000 arasında olmalı.");
        RuleFor(x => x.MaxContacts).InclusiveBetween(0, 1_000_000).When(x => x.MaxContacts is not null)
            .WithMessage("Müşteri sınırı 0 ile 1.000.000 arasında olmalı.");
        RuleFor(x => x.MaxAppointmentsPerMonth).InclusiveBetween(0, 100_000).When(x => x.MaxAppointmentsPerMonth is not null)
            .WithMessage("Aylık randevu sınırı 0 ile 100.000 arasında olmalı.");
        RuleForEach(x => x.Features).Must(f => AdminPlans.FeatureFromKey(f) is not null).WithMessage("Bilinmeyen özellik.");
    }
}
