using FluentValidation;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Staff;

namespace KobiFabrika.Api.Features.Appointments;

/// <summary>
/// Randevu. Saatler UTC. SaleNo: satışa çevrildiyse satış numarası. CustomerPackageId/PackageName: paketten kullanılan randevu
/// (PackageSessionUsed: tamamlandı ve seans düştü). CancelSource: Business veya Customer (online iptal).
/// </summary>
public sealed record AppointmentResponse(
    Guid Id, Guid ContactId, string ContactName, string? ContactPhone, Guid StaffMemberId, string StaffName, Guid ProductId, string ServiceName,
    DateTime StartAt, DateTime EndAt, AppointmentStatus Status, AppointmentSource Source, string? Note, Guid? SaleId, string? SaleNo,
    DateTime CreatedAt, string? CreatedByName, DateTime? CancelledAt, string? CancelReason, Guid? CustomerPackageId, string? PackageName,
    bool PackageSessionUsed, bool CancelledByCustomer);

/// <summary>EndAt boşsa hizmet süresinden hesaplanır. CustomerPackageId: müşterinin bu hizmete ait paketinden kullan.</summary>
public sealed record AppointmentRequest(
    Guid ContactId, Guid StaffMemberId, Guid ProductId, DateTime StartAt, DateTime? EndAt, string? Note, Guid? CustomerPackageId = null);

/// <summary>Taşıma, süre değiştirme, personel/hizmet değiştirme, paket ve not. NotifyCustomer: saat değiştiyse müşteriye SMS.</summary>
public sealed record AppointmentUpdateRequest(
    Guid StaffMemberId, Guid ProductId, DateTime StartAt, DateTime EndAt, string? Note, Guid? CustomerPackageId = null, bool NotifyCustomer = false);

/// <summary>NotifyCustomer: müşteriye iptal SMS'i (işletme telefonuyla). Ret (Beklemede) her zaman SMS'le bildirilir.</summary>
public sealed record CancelAppointmentRequest(string? Reason, bool NotifyCustomer = false);

/// <summary>Kaydetmeden önce uyarılar: çakışma, çalışma saati/kapalı zaman dışı, gelmeyen müşteri.</summary>
public sealed record AppointmentCheckRequest(Guid? ContactId, Guid StaffMemberId, DateTime StartAt, DateTime EndAt, Guid? ExcludeId);

public sealed record AppointmentCheckResponse(string? Overlap, string? OutsideHours, NoShowWarning? NoShow);

/// <summary>Kayıt sonrası bilgi: çalışma saati dışı uyarısı (engellemez).</summary>
public sealed record AppointmentSavedResponse(AppointmentResponse Appointment, IReadOnlyList<string> Warnings);

/// <summary>Satışa çevirme: hızlı satış ekranını müşteri, hizmet ve personelle doldurur.</summary>
public sealed record AppointmentSaleDraft(
    Guid AppointmentId, Guid ContactId, string ContactName, Guid ProductId, string Description, decimal UnitPrice, Guid StaffMemberId, string StaffName);

public sealed record CalendarStaffDay(DateOnly Date, IReadOnlyList<TimeRangeDto> Ranges);

/// <summary>Takvim kolonu: personel ve gün bazında çalışma aralıkları (işletme saati ve tatil hesaba katılmış).</summary>
public sealed record CalendarStaff(Guid Id, string FullName, string? Title, StaffColor Color, IReadOnlyList<CalendarStaffDay> Days);

/// <summary>İşletmenin günü: kapalı mı, tatil adı, açılış-kapanış.</summary>
public sealed record CalendarDay(DateOnly Date, bool Closed, string? HolidayName, string? Opens, string? Closes);

/// <summary>
/// Takvim. OnlyOwn: Personel rolü ve "sadece kendi randevuları" ayarı açık (Staff ve Appointments sadece kendi kaydı).
/// MyStaffId: oturumdaki kullanıcıya bağlı personel. Saatler UTC, aralıklar yerel "HH:mm".
/// </summary>
public sealed record CalendarResponse(
    DateOnly From, DateOnly To, IReadOnlyList<CalendarDay> Days, IReadOnlyList<CalendarStaff> Staff, IReadOnlyList<AppointmentResponse> Appointments,
    IReadOnlyList<TimeOffResponse> TimeOffs, bool OnlyOwn, Guid? MyStaffId);

/// <summary>Müşterinin randevuları (yeniden eskiye) ve gelmeyen müşteri uyarısı (son 5 randevudan 2+ gelmedi).</summary>
public sealed record ContactAppointmentsResponse(IReadOnlyList<AppointmentResponse> Items, NoShowWarning? NoShow);

public sealed record HolidayRequest(DateOnly Date, string Name);

public sealed record HolidayResponse(Guid Id, DateOnly Date, string Name);

public sealed record AppointmentSettingsResponse(bool StaffSeesOnlyOwn);

public sealed record AppointmentSettingsRequest(bool StaffSeesOnlyOwn);

public static class AppointmentLabels
{
    public static readonly IReadOnlyDictionary<AppointmentStatus, string> Statuses = new Dictionary<AppointmentStatus, string>
    {
        [AppointmentStatus.Pending] = "Beklemede",
        [AppointmentStatus.Confirmed] = "Onaylı",
        [AppointmentStatus.Completed] = "Tamamlandı",
        [AppointmentStatus.NoShow] = "Gelmedi",
        [AppointmentStatus.Cancelled] = "İptal",
    };
}

internal static class AppointmentTimes
{
    public static bool WholeMinute(DateTime value) => value.Second == 0 && value.Millisecond == 0 && value.Microsecond == 0;

    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
}

public sealed class AppointmentRequestValidator : AbstractValidator<AppointmentRequest>
{
    public AppointmentRequestValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty().WithMessage("Müşteri seçin.");
        RuleFor(x => x.StaffMemberId).NotEmpty().WithMessage("Personel seçin.");
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Hizmet seçin.");
        RuleFor(x => x.StartAt).Must(AppointmentTimes.WholeMinute).WithMessage("Başlangıç saatini dakika olarak girin.");
        RuleFor(x => x.EndAt).Must(e => e is null || AppointmentTimes.WholeMinute(e.Value)).WithMessage("Bitiş saatini dakika olarak girin.");
        RuleFor(x => x.Note).MaximumLength(Appointment.MaxNoteLength).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}

public sealed class AppointmentUpdateRequestValidator : AbstractValidator<AppointmentUpdateRequest>
{
    public AppointmentUpdateRequestValidator()
    {
        RuleFor(x => x.StaffMemberId).NotEmpty().WithMessage("Personel seçin.");
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Hizmet seçin.");
        RuleFor(x => x.StartAt).Must(AppointmentTimes.WholeMinute).WithMessage("Başlangıç saatini dakika olarak girin.");
        RuleFor(x => x.EndAt).Must(AppointmentTimes.WholeMinute).WithMessage("Bitiş saatini dakika olarak girin.");
        RuleFor(x => x.Note).MaximumLength(Appointment.MaxNoteLength).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}

public sealed class CancelAppointmentRequestValidator : AbstractValidator<CancelAppointmentRequest>
{
    public CancelAppointmentRequestValidator() =>
        RuleFor(x => x.Reason).MaximumLength(500).WithMessage("İptal nedeni en fazla 500 karakter olabilir.");
}

public sealed class HolidayRequestValidator : AbstractValidator<HolidayRequest>
{
    public HolidayRequestValidator() =>
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Adını girin (ör. Kurban Bayramı).")
            .MaximumLength(Holiday.MaxNameLength).WithMessage("Ad en fazla 100 karakter olabilir.");
}
