using System.Globalization;
using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Businesses;
using KobiFabrika.Domain.Staff;

namespace KobiFabrika.Api.Features.Staff;

/// <summary>
/// ServiceIds boşsa personel bütün hizmetleri verir (AllServices = true). StartDate, BirthDate ve Notes Personel rolüne null döner
/// (takvim için liste herkese açık; kişisel bilgiler değil). Maaş bu yanıtta hiç yoktur: <c>/api/staff/{id}/salary</c>.
/// </summary>
public sealed record StaffResponse(
    Guid Id, string FullName, string? Title, string? Phone, StaffColor Color, bool ShowInBooking, Guid? UserId, string? UserName,
    bool IsActive, int SortOrder, bool AllServices, IReadOnlyList<Guid> ServiceIds, DateOnly? StartDate, DateOnly? BirthDate, string? Notes);

public sealed record StaffRequest(
    string FullName, string? Title, string? Phone, StaffColor Color, bool ShowInBooking, Guid? UserId, bool IsActive,
    DateOnly? StartDate = null, DateOnly? BirthDate = null, string? Notes = null);

/// <summary>
/// Aylık maaş (bilgi amaçlı; bordro hesabı yok). Salary null: girilmemiş (Basis o zaman anlamsız, Net döner).
/// Basis null değil: OpenAPI'de nullable enum ortak şemayı bozar (CLAUDE.md).
/// </summary>
public sealed record StaffSalaryDto(decimal? Salary, SalaryBasis Basis);

/// <summary>Personele bağlı maaş gideri (ters kayıtlar dahil; NetAmount ters kayıtta eksi).</summary>
public sealed record SalaryPaymentItem(
    Guid Id, DateOnly Date, decimal Amount, decimal NetAmount, string Description, string? DocumentNo, string CashAccountName,
    bool IsReversal, bool IsReversed);

public sealed record SalaryPaymentsResponse(IReadOnlyList<SalaryPaymentItem> Items, decimal Total);

/// <summary>Saat aralığı "HH:mm" (yerel saat), Start &lt; End.</summary>
public sealed record TimeRangeDto(string Start, string End);

public sealed record StaffDayDto(DayOfWeek Day, IReadOnlyList<TimeRangeDto> Ranges);

/// <summary>Haftanın 7 günü; çalışmadığı gün boş liste. Gün başına en fazla 4 aralık (ör. öğle arası için 2).</summary>
public sealed record StaffWorkingHoursDto(IReadOnlyList<StaffDayDto> Days);

/// <summary>Boş liste: personel bütün hizmetleri verir.</summary>
public sealed record StaffServicesRequest(IReadOnlyList<Guid> ProductIds);

public sealed record TimeOffRequest(DateTime StartAt, DateTime EndAt, TimeOffKind Kind, string? Note);

/// <summary>LeaveRequestId dolu: onaylı izin talebinden; sadece izin iptaliyle kalkar.</summary>
public sealed record TimeOffResponse(Guid Id, Guid StaffMemberId, DateTime StartAt, DateTime EndAt, TimeOffKind Kind, string? Note, Guid? LeaveRequestId);

/// <summary>Personel rolündeki kullanıcıya bağlı personel kaydı ("Ben").</summary>
public sealed record MyStaffResponse(Guid? StaffMemberId);

public static class TimeText
{
    public static bool IsTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static TimeOnly Parse(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    public static string Format(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public sealed class StaffRequestValidator : AbstractValidator<StaffRequest>
{
    public StaffRequestValidator()
    {
        RuleFor(x => x.FullName).Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 2).WithMessage("Ad soyad girin.")
            .MaximumLength(StaffMember.MaxNameLength).WithMessage("Ad soyad en fazla 100 karakter olabilir.");
        RuleFor(x => x.Title).MaximumLength(100).WithMessage("Unvan en fazla 100 karakter olabilir.");
        RuleFor(x => x.Phone).ValidPhone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Color).IsInEnum().WithMessage("Takvim rengini seçin.");
        RuleFor(x => x.StartDate).Must(d => d is null || (d.Value.Year >= 1950 && d.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)))
            .WithMessage("İşe başlama tarihini kontrol edin.");
        RuleFor(x => x.BirthDate).Must(d => d is null || (d.Value.Year >= 1900 && d.Value < DateOnly.FromDateTime(DateTime.UtcNow)))
            .WithMessage("Doğum tarihini kontrol edin.");
        RuleFor(x => x).Must(x => x.StartDate is null || x.BirthDate is null || x.BirthDate < x.StartDate).WithName("birthDate")
            .WithMessage("Doğum tarihi işe başlama tarihinden önce olmalı.");
        RuleFor(x => x.Notes).MaximumLength(StaffMember.MaxNotesLength).WithMessage("Not en fazla 1000 karakter olabilir.");
    }
}

public sealed class StaffSalaryDtoValidator : AbstractValidator<StaffSalaryDto>
{
    public StaffSalaryDtoValidator()
    {
        RuleFor(x => x.Salary).ValidMoney().When(x => x.Salary is not null);
        RuleFor(x => x.Basis).IsInEnum().WithMessage("Brüt veya net seçin.");
    }
}

public sealed class StaffWorkingHoursValidator : AbstractValidator<StaffWorkingHoursDto>
{
    public const int MaxRangesPerDay = 4;

    public StaffWorkingHoursValidator()
    {
        RuleFor(x => x.Days)
            .NotNull()
            .Must(d => d.Count == 7 && d.Select(x => x.Day).Distinct().Count() == 7)
            .WithMessage("Haftanın 7 günü için çalışma saati girin.");

        RuleForEach(x => x.Days).ChildRules(day =>
        {
            day.RuleFor(d => d.Day).IsInEnum();
            day.RuleFor(d => d.Ranges).NotNull()
                .Must(r => r.Count <= MaxRangesPerDay).WithMessage(d => $"{TurkishDayNames.Of(d.Day)}: en fazla 4 aralık girilebilir.");
            day.RuleForEach(d => d.Ranges).ChildRules(range =>
            {
                range.RuleFor(r => r.Start).Must(TimeText.IsTime).WithMessage("Başlangıç saatini SS:dd biçiminde girin.");
                range.RuleFor(r => r.End).Must(TimeText.IsTime).WithMessage("Bitiş saatini SS:dd biçiminde girin.");
            });
            day.RuleFor(d => d)
                .Must(d => d.Ranges is null || d.Ranges.All(r => !TimeText.IsTime(r.Start) || !TimeText.IsTime(r.End) || TimeText.Parse(r.Start) < TimeText.Parse(r.End)))
                .WithName("ranges")
                .WithMessage(d => $"{TurkishDayNames.Of(d.Day)}: bitiş saati başlangıçtan sonra olmalı.");
            day.RuleFor(d => d)
                .Must(d => d.Ranges is null || !Overlapping(d.Ranges))
                .WithName("ranges")
                .WithMessage(d => $"{TurkishDayNames.Of(d.Day)}: aralıklar çakışıyor.");
        });
    }

    private static bool Overlapping(IReadOnlyList<TimeRangeDto> ranges)
    {
        var valid = ranges.Where(r => TimeText.IsTime(r.Start) && TimeText.IsTime(r.End))
            .Select(r => (Start: TimeText.Parse(r.Start), End: TimeText.Parse(r.End)))
            .OrderBy(r => r.Start)
            .ToList();
        return valid.Zip(valid.Skip(1)).Any(p => p.Second.Start < p.First.End);
    }
}

public sealed class StaffServicesRequestValidator : AbstractValidator<StaffServicesRequest>
{
    public StaffServicesRequestValidator() =>
        RuleFor(x => x.ProductIds).NotNull().Must(p => p.Count <= 500).WithMessage("En fazla 500 hizmet seçilebilir.");
}

public sealed class TimeOffRequestValidator : AbstractValidator<TimeOffRequest>
{
    public TimeOffRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Türü seçin.");
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).WithMessage("Bitiş başlangıçtan sonra olmalı.");
        RuleFor(x => x).Must(x => x.EndAt - x.StartAt <= TimeSpan.FromDays(366)).WithName("endAt")
            .WithMessage("Kapalı zaman en fazla 1 yıl olabilir.");
        RuleFor(x => x.Note).MaximumLength(200).WithMessage("Not en fazla 200 karakter olabilir.");
    }
}

public static class StaffLabels
{
    public static readonly IReadOnlyDictionary<TimeOffKind, string> TimeOffKinds = new Dictionary<TimeOffKind, string>
    {
        [TimeOffKind.Break] = "Mola",
        [TimeOffKind.Leave] = "İzin",
        [TimeOffKind.Other] = "Kapalı",
    };
}
