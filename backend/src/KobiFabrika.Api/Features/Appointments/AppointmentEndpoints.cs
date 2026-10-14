using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Appointments;

/// <summary>Randevular modülü kapalıysa (Ayarlar → Modüller) randevu uçları 409 <c>appointments_module_disabled</c>.</summary>
internal sealed class AppointmentModuleFilter(AppDbContext db, CurrentMember member) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (member.BusinessId is { } businessId && !await AppointmentEndpoints.ModuleEnabledAsync(db, businessId, context.HttpContext.RequestAborted))
        {
            return Problems.Conflict("appointments_module_disabled", "Randevular modülü kapalı. Ayarlar → Modüller'den açabilirsiniz.");
        }

        return await next(context);
    }
}

/// <summary>
/// Takvim ve randevular. Her rol takvimi görür; Sahip, Yönetici ve Personel randevu açar, taşır ve durumunu değiştirir
/// (karar a: küçük salonlarda resepsiyonu personel yapar). "Personel sadece kendi randevularını görsün" açıksa Personel rolü
/// sadece bağlı olduğu personelin randevularını görür ve yazar. Mali Müşavir salt okunur.
/// </summary>
public static class AppointmentEndpoints
{
    public const int MaxCalendarDays = 14;

    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments").WithTags("Appointments").RequireAuthorization(Policies.Panel)
            .AddEndpointFilter<AppointmentModuleFilter>();

        group.MapGet("/{id:guid}", Get).WithSummary("Randevu");
        group.MapPost("/", Create).RequireAuthorization(Policies.CanWrite).Validate<AppointmentRequest>()
            .Produces<AppointmentSavedResponse>(StatusCodes.Status201Created)
            .WithSummary("Randevu ver (bitiş boşsa hizmet süresinden). Çakışma 409 appointment_overlap");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.CanWrite).Validate<AppointmentUpdateRequest>()
            .Produces<AppointmentSavedResponse>().WithSummary("Taşı, süreyi/personeli/hizmeti değiştir, not");
        group.MapPost("/check", Check).RequireAuthorization(Policies.CanWrite)
            .WithSummary("Kaydetmeden önce uyarılar: çakışma, çalışma saati dışı, gelmeyen müşteri");
        group.MapPost("/{id:guid}/confirm", (Guid id, CurrentMember m, AppDbContext db, AppointmentNotifier n, TimeProvider c, CancellationToken ct) =>
                ChangeStatusAsync(id, AppointmentStatus.Confirmed, null, m, db, n, c, ct))
            .RequireAuthorization(Policies.CanWrite).Produces<AppointmentResponse>().WithSummary("Onayla (Beklemede → Onaylı; müşteriye onay SMS'i)");
        group.MapPost("/{id:guid}/complete", (Guid id, CurrentMember m, AppDbContext db, AppointmentNotifier n, TimeProvider c, CancellationToken ct) =>
                ChangeStatusAsync(id, AppointmentStatus.Completed, null, m, db, n, c, ct))
            .RequireAuthorization(Policies.CanWrite).Produces<AppointmentResponse>()
            .WithSummary("Tamamlandı (paketten kullanılıyorsa seans düşer; seans yoksa 409 package_exhausted)");
        group.MapPost("/{id:guid}/no-show", (Guid id, CurrentMember m, AppDbContext db, AppointmentNotifier n, TimeProvider c, CancellationToken ct) =>
                ChangeStatusAsync(id, AppointmentStatus.NoShow, null, m, db, n, c, ct))
            .RequireAuthorization(Policies.CanWrite).Produces<AppointmentResponse>().WithSummary("Gelmedi");
        group.MapPost("/{id:guid}/cancel", (Guid id, CancelAppointmentRequest request, CurrentMember m, AppDbContext db, AppointmentNotifier n, TimeProvider c, CancellationToken ct) =>
                ChangeStatusAsync(id, AppointmentStatus.Cancelled, request.Reason, m, db, n, c, ct, notify: request.NotifyCustomer))
            .RequireAuthorization(Policies.CanWrite).Validate<CancelAppointmentRequest>().Produces<AppointmentResponse>()
            .WithSummary("İptal et (notifyCustomer: müşteriye SMS)");
        group.MapPost("/{id:guid}/reject", (Guid id, CancelAppointmentRequest request, CurrentMember m, AppDbContext db, AppointmentNotifier n, TimeProvider c, CancellationToken ct) =>
                ChangeStatusAsync(id, AppointmentStatus.Cancelled, request.Reason, m, db, n, c, ct, reject: true))
            .RequireAuthorization(Policies.CanWrite).Validate<CancelAppointmentRequest>().Produces<AppointmentResponse>()
            .WithSummary("Reddet (Beklemede → İptal; müşteriye onaylanmadı SMS'i)");
        group.MapPost("/{id:guid}/reopen", (Guid id, CurrentMember m, AppDbContext db, AppointmentNotifier n, TimeProvider c, CancellationToken ct) =>
                ChangeStatusAsync(id, AppointmentStatus.Confirmed, null, m, db, n, c, ct, reopen: true))
            .RequireAuthorization(Policies.CanWrite).Produces<AppointmentResponse>()
            .WithSummary("Geri al: Tamamlandı (satışa çevrilmemişse; paket seansı iade) veya Gelmedi → Onaylı");
        group.MapPost("/{id:guid}/notify-moved", NotifyMoved).RequireAuthorization(Policies.CanWrite)
            .Produces<NotifyMovedResponse>()
            .WithSummary("Sürükle-bırakla taşınan randevu için müşteriye SMS (son saat değişikliğinden sonra bir kez)");
        group.MapGet("/{id:guid}/sale-draft", SaleDraft).RequireAuthorization(Policies.CanWrite)
            .WithSummary("Satışa çevir: hızlı satış için müşteri, hizmet ve personel");

        app.MapGet("/api/calendar", Calendar).WithTags("Appointments").RequireAuthorization(Policies.Panel)
            .AddEndpointFilter<AppointmentModuleFilter>().Produces<CalendarResponse>()
            .WithSummary("Takvim: from-to (en fazla 14 gün) randevular, personel çalışma aralıkları, kapalı zamanlar, tatiller");
        app.MapGet("/api/contacts/{id:guid}/appointments", ContactAppointments).WithTags("Appointments").RequireAuthorization(Policies.Panel)
            .AddEndpointFilter<AppointmentModuleFilter>().Produces<ContactAppointmentsResponse>()
            .WithSummary("Müşterinin randevuları (yeniden eskiye) ve gelmeyen müşteri uyarısı");

        var holidays = app.MapGroup("/api/holidays").WithTags("Appointments").RequireAuthorization(Policies.Panel);
        holidays.MapGet("/", ListHolidays).WithSummary("Tatil ve kapalı günler (from/to; varsayılan bugünden 1 yıl)");
        holidays.MapPost("/", CreateHoliday).RequireAuthorization(Policies.ManageBusiness).Validate<HolidayRequest>()
            .Produces<HolidayResponse>(StatusCodes.Status201Created).WithSummary("Tatil / kapalı gün ekle");
        holidays.MapDelete("/{id:guid}", DeleteHoliday).RequireAuthorization(Policies.ManageBusiness).WithSummary("Tatil / kapalı günü sil");

        app.MapGet("/api/business/appointment-settings", GetSettings).WithTags("Business").RequireAuthorization(Policies.ViewSettings)
            .WithSummary("Randevu ayarları");
        app.MapPut("/api/business/appointment-settings", UpdateSettings).WithTags("Business").RequireAuthorization(Policies.ManageBusiness)
            .WithSummary("Randevu ayarları: personel sadece kendi randevularını görsün");

        return app;
    }

    public static async Task<bool> ModuleEnabledAsync(AppDbContext db, Guid businessId, CancellationToken ct) =>
        (await db.Businesses.Where(b => b.Id == businessId).Select(b => b.Modules).FirstAsync(ct)).HasFlag(BusinessModules.Appointments);

    private static ProblemHttpResult NotFound() => Problems.NotFound("Randevu bulunamadı.");

    /// <summary>
    /// Masaüstünde sürükle-bırakla taşınan randevuda "Müşteriye SMS ile bildirilsin mi? [Gönder]". Sadece saati taşınmış,
    /// taşınabilir durumdaki randevuda ve müşterinin cep telefonu varsa; son saat değişikliğinden sonra bir kez (409 already_notified).
    /// </summary>
    private static async Task<IResult> NotifyMoved(
        Guid id, CurrentMember member, AppDbContext db, AppointmentNotifier notifier, CancellationToken ct)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var a = await db.Appointments.Visible(scope).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null)
        {
            return NotFound();
        }

        if (a.TimeSetAt <= a.CreatedAt || !AppointmentRules.IsMovable(a.Status, a.SaleId is not null))
        {
            return Problems.Conflict("not_moved", "Bu randevu taşınmadı; bildirilecek değişiklik yok.");
        }

        var phone = await db.Contacts.Where(c => c.Id == a.ContactId).Select(c => c.Phone).FirstOrDefaultAsync(ct);
        if (phone is null || !PhoneVerification.IsMobile(phone))
        {
            return Problems.Conflict("no_mobile", "Müşterinin cep telefonu kayıtlı değil.");
        }

        var alreadySent = await db.MessageLogs.AnyAsync(m => m.RefType == "Appointment" && m.RefId == id
            && m.Purpose == Domain.Messaging.MessagePurpose.AppointmentMoved && m.CreatedAt >= a.TimeSetAt, ct);
        if (alreadySent)
        {
            return Problems.Conflict("already_notified", "Müşteriye bu değişiklik için SMS zaten gönderildi.");
        }

        var log = await notifier.SendAsync(id, AppointmentSmsKind.Moved, ct);
        return TypedResults.Ok(new NotifyMovedResponse(log?.Status ?? Domain.Messaging.MessageStatus.Failed));
    }

    private static ProblemHttpResult OwnOnly() =>
        Problems.Forbidden("own_appointments_only", "Sadece kendi randevularınızı düzenleyebilirsiniz.");

    /// <summary>Kısıtlı Personel sadece kendi personel kaydının randevularını görür.</summary>
    private static IQueryable<Appointment> Visible(this IQueryable<Appointment> query, (bool Restricted, Guid? StaffId) scope) =>
        scope.Restricted ? query.Where(a => a.StaffMemberId == scope.StaffId) : query;

    internal static Task<List<AppointmentResponse>> ProjectAsync(AppDbContext db, IQueryable<Appointment> query, CancellationToken ct) =>
        query.AsNoTracking().Select(a => new AppointmentResponse(
                a.Id,
                a.ContactId,
                db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == a.ContactId).Select(c => c.DisplayName).FirstOrDefault() ?? "",
                db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == a.ContactId).Select(c => c.Phone).FirstOrDefault(),
                a.StaffMemberId,
                db.StaffMembers.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(s => s.Id == a.StaffMemberId).Select(s => s.FullName).FirstOrDefault() ?? "",
                a.ProductId,
                db.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(p => p.Id == a.ProductId).Select(p => p.Name).FirstOrDefault() ?? "",
                a.StartAt,
                a.EndAt,
                a.Status,
                a.Source,
                a.Note,
                a.SaleId,
                db.Sales.Where(s => s.Id == a.SaleId).Select(s => s.No).FirstOrDefault(),
                a.CreatedAt,
                db.Users.Where(u => u.Id == a.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
                a.CancelledAt,
                a.CancelReason,
                a.CustomerPackageId,
                db.CustomerPackages.Where(p => p.Id == a.CustomerPackageId).Select(p => p.Name).FirstOrDefault(),
                a.PackageSessionUsed,
                a.CancelSource == AppointmentCancelSource.Customer))
            .ToListAsync(ct);

    private static async Task<AppointmentResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        (await ProjectAsync(db, db.Appointments.Where(a => a.Id == id), ct)).FirstOrDefault();

    private static async Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> Get(Guid id, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        return await db.Appointments.Visible(scope).AnyAsync(a => a.Id == id, ct) && await LoadAsync(db, id, ct) is { } appointment
            ? TypedResults.Ok(appointment)
            : NotFound();
    }

    /// <summary>Hizmet, personel ve saat doğrulaması (oluşturma ve güncelleme ortak). Hata yoksa null.</summary>
    private static async Task<(IResult? Problem, Product? Service)> ValidateAsync(
        AppDbContext db, Guid staffId, Guid productId, DateTime start, DateTime end, DateTime now, bool allowInactiveService, CancellationToken ct)
    {
        var service = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.Type == ProductType.Service, ct);
        if (service is null)
        {
            return (Validation.FieldProblem("productId", "Hizmet bulunamadı."), null);
        }

        if (!service.IsActive && !allowInactiveService)
        {
            return (Validation.FieldProblem("productId", $"{service.Name} pasif; randevu verilemez."), null);
        }

        var staff = await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == staffId, ct);
        if (staff is null || !staff.IsActive)
        {
            return (Validation.FieldProblem("staffMemberId", "Personel bulunamadı veya pasif."), null);
        }

        var services = await db.StaffServices.Where(x => x.StaffMemberId == staffId).Select(x => x.ProductId).ToListAsync(ct);
        if (services.Count > 0 && !services.Contains(productId))
        {
            return (Validation.FieldProblem("productId", $"{staff.FullName} bu hizmeti vermiyor."), null);
        }

        var minutes = (end - start).TotalMinutes;
        if (minutes < AppointmentRules.MinDurationMinutes || minutes > AppointmentRules.MaxDurationMinutes)
        {
            return (Validation.FieldProblem("endAt", "Randevu süresi 5 dakika ile 12 saat arasında olmalı."), null);
        }

        if (BusinessTime.LocalDay(start) != BusinessTime.LocalDay(end.AddMinutes(-1)))
        {
            return (Validation.FieldProblem("endAt", "Randevu gece yarısını aşamaz."), null);
        }

        if (start < now.AddYears(-1) || start > now.AddYears(2))
        {
            return (Validation.FieldProblem("startAt", "Randevu tarihi 1 yıl öncesi ile 2 yıl sonrası arasında olmalı."), null);
        }

        return (null, service);
    }

    /// <summary>Çalışma saati veya kapalı zaman dışındaysa uyarı metni (engellemez).</summary>
    private static async Task<string?> OutsideHoursAsync(AppDbContext db, Guid staffId, DateTime start, DateTime end, CancellationToken ct)
    {
        var day = BusinessTime.LocalDay(start);
        var plans = await AvailabilityQueries.LoadAsync(db, [staffId], day, day, includeAppointments: false, null, ct);
        var plan = plans[(staffId, day)];
        if (Availability.Fits(plan, start, end))
        {
            return null;
        }

        var name = await db.StaffMembers.Where(s => s.Id == staffId).Select(s => s.FullName).FirstAsync(ct);
        return plan.HolidayName is { } holiday ? $"{day:dd.MM.yyyy} tatil ({holiday})."
            : plan.BusinessClosed ? "İşletme bu gün kapalı."
            : $"{name} bu saatte çalışmıyor veya kapalı (mola/izin).";
    }

    /// <summary>Paket müşterinin, bu hizmete ait, aktif, seansı kalan ve randevu gününde geçerli olmalı.</summary>
    private static async Task<IResult?> ValidatePackageAsync(
        AppDbContext db, Guid? packageId, Guid contactId, Guid productId, DateTime start, CancellationToken ct)
    {
        if (packageId is not { } id)
        {
            return null;
        }

        var package = await db.CustomerPackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.ContactId == contactId, ct);
        if (package is null)
        {
            return Validation.FieldProblem("customerPackageId", "Paket bulunamadı.");
        }

        if (package.ProductId != productId)
        {
            return Validation.FieldProblem("customerPackageId", "Bu paket seçilen hizmet için değil.");
        }

        return PackageRules.UsableOn(package.Status, package.TotalSessions, package.UsedSessions, package.ExpiresOn, BusinessTime.LocalDay(start))
            ? null
            : Validation.FieldProblem("customerPackageId", "Paketin seansı kalmadı, süresi bu tarihten önce doluyor veya paket kapatıldı.");
    }

    private static async Task<IResult> Create(
        AppointmentRequest request, CurrentMember member, AppDbContext db, AppointmentBooking booking, TimeProvider clock, CancellationToken ct)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        if (scope.Restricted && request.StaffMemberId != scope.StaffId)
        {
            return OwnOnly();
        }

        if (!await db.Contacts.AnyAsync(c => c.Id == request.ContactId, ct))
        {
            return Validation.FieldProblem("contactId", "Müşteri bulunamadı.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var start = AppointmentTimes.Utc(request.StartAt);
        var duration = await db.Products.Where(p => p.Id == request.ProductId).Select(p => p.DurationMinutes).FirstOrDefaultAsync(ct);
        DateTime end;
        if (request.EndAt is { } requestedEnd)
        {
            end = AppointmentTimes.Utc(requestedEnd);
        }
        else if (duration is > 0)
        {
            end = start.AddMinutes(duration.Value);
        }
        else
        {
            return Validation.FieldProblem("endAt", "Hizmetin süresi tanımlı değil; bitiş saatini girin.");
        }

        var (problem, _) = await ValidateAsync(db, request.StaffMemberId, request.ProductId, start, end, now, false, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await ValidatePackageAsync(db, request.CustomerPackageId, request.ContactId, request.ProductId, start, ct) is { } packageProblem)
        {
            return packageProblem;
        }

        var appointment = new Appointment
        {
            ContactId = request.ContactId,
            StaffMemberId = request.StaffMemberId,
            ProductId = request.ProductId,
            StartAt = start,
            EndAt = end,
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.Panel,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ConfirmedAt = now,
            TimeSetAt = now,
            CustomerPackageId = request.CustomerPackageId,
        };
        db.Appointments.Add(appointment);
        await booking.SaveAsync(appointment, ct);

        var warnings = new List<string>();
        if (await OutsideHoursAsync(db, appointment.StaffMemberId, start, end, ct) is { } outside)
        {
            warnings.Add(outside);
        }

        return TypedResults.Created($"/api/appointments/{appointment.Id}",
            new AppointmentSavedResponse((await LoadAsync(db, appointment.Id, ct))!, warnings));
    }

    private static async Task<IResult> Update(
        Guid id, AppointmentUpdateRequest request, CurrentMember member, AppDbContext db, AppointmentBooking booking, AppointmentNotifier notifier,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var appointment = await db.Appointments.Visible(scope).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
        {
            return NotFound();
        }

        if (scope.Restricted && request.StaffMemberId != scope.StaffId)
        {
            return OwnOnly();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var start = AppointmentTimes.Utc(request.StartAt);
        var end = AppointmentTimes.Utc(request.EndAt);
        var timeChanged = start != appointment.StartAt || end != appointment.EndAt;
        var packageChanged = request.CustomerPackageId != appointment.CustomerPackageId;
        var moved = timeChanged || request.StaffMemberId != appointment.StaffMemberId || request.ProductId != appointment.ProductId;
        if ((moved || packageChanged) && !AppointmentRules.IsMovable(appointment.Status, appointment.SaleId is not null))
        {
            return Problems.Conflict("appointment_locked", "Tamamlanan, gelmedi işaretlenen, iptal edilen veya satışa çevrilen randevu taşınamaz.");
        }

        if (moved)
        {
            var (problem, _) = await ValidateAsync(db, request.StaffMemberId, request.ProductId, start, end, now,
                allowInactiveService: request.ProductId == appointment.ProductId, ct);
            if (problem is not null)
            {
                return problem;
            }
        }

        if ((moved || packageChanged)
            && await ValidatePackageAsync(db, request.CustomerPackageId, appointment.ContactId, request.ProductId, start, ct) is { } packageProblem)
        {
            return packageProblem;
        }

        appointment.CustomerPackageId = request.CustomerPackageId;
        appointment.StaffMemberId = request.StaffMemberId;
        appointment.ProductId = request.ProductId;
        appointment.StartAt = start;
        appointment.EndAt = end;
        appointment.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (timeChanged)
        {
            // Taşınan randevunun hatırlatması yeniden hesaplanır (kısa süre kala taşındıysa gitmez).
            appointment.TimeSetAt = now;
            appointment.ReminderSentAt = null;
        }

        if (moved)
        {
            await booking.SaveAsync(appointment, ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        var warnings = new List<string>();
        if (moved && await OutsideHoursAsync(db, appointment.StaffMemberId, start, end, ct) is { } outside)
        {
            warnings.Add(outside);
        }

        if (timeChanged && request.NotifyCustomer)
        {
            await notifier.SendAsync(id, AppointmentSmsKind.Moved, ct);
        }

        return TypedResults.Ok(new AppointmentSavedResponse((await LoadAsync(db, id, ct))!, warnings));
    }

    private static async Task<Ok<AppointmentCheckResponse>> Check(
        AppointmentCheckRequest request, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var start = AppointmentTimes.Utc(request.StartAt);
        var end = AppointmentTimes.Utc(request.EndAt);
        string? overlap = null, outside = null;
        if (end > start && (end - start).TotalMinutes <= AppointmentRules.MaxDurationMinutes
            && await db.StaffMembers.AnyAsync(s => s.Id == request.StaffMemberId && s.IsActive, ct))
        {
            overlap = await AppointmentBooking.OverlapMessageAsync(db, request.StaffMemberId, start, end, request.ExcludeId, ct);
            outside = await OutsideHoursAsync(db, request.StaffMemberId, start, end, ct);
        }

        var noShow = request.ContactId is { } contactId ? await NoShowAsync(db, contactId, clock, ct) : null;
        return TypedResults.Ok(new AppointmentCheckResponse(overlap, outside, noShow));
    }

    internal static async Task<NoShowWarning?> NoShowAsync(AppDbContext db, Guid contactId, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var history = await db.Appointments.AsNoTracking()
            .Where(a => a.ContactId == contactId && a.StartAt <= now
                && (a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.NoShow))
            .OrderByDescending(a => a.StartAt).Take(NoShowRule.Window)
            .Select(a => new { a.StartAt, a.Status })
            .ToListAsync(ct);
        return NoShowRule.Evaluate(history.Select(h => (h.StartAt, h.Status)), now);
    }

    /// <summary>
    /// Durum değişikliği koşullu UPDATE ile bir kez yapılır (eşzamanlı iki tıklama ikinci kez uygulanmaz).
    /// Tamamlandı müşterinin son ziyaretini günceller; Tamamlandı'nın geri alınması yeniden hesaplar.
    /// </summary>
    private static async Task<IResult> ChangeStatusAsync(
        Guid id, AppointmentStatus to, string? reason, CurrentMember member, AppDbContext db, AppointmentNotifier notifier, TimeProvider clock,
        CancellationToken ct, bool reopen = false, bool notify = false, bool reject = false)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var appointment = await db.Appointments.AsNoTracking().Visible(scope).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
        {
            return NotFound();
        }

        var from = appointment.Status;
        var reopening = from is AppointmentStatus.Completed or AppointmentStatus.NoShow;
        if (reject && from != AppointmentStatus.Pending)
        {
            return Problems.Conflict("invalid_status", "Sadece onay bekleyen randevu reddedilir.");
        }

        if (reopen != reopening || !AppointmentRules.CanTransition(from, to, appointment.SaleId is not null))
        {
            return appointment.SaleId is not null && from == AppointmentStatus.Completed
                ? Problems.Conflict("appointment_converted", "Satışa çevrilen randevu geri alınamaz; önce satışı iptal edin.")
                : Problems.Conflict("invalid_status", $"Randevu {AppointmentLabels.Statuses[from].ToLowerInvariant()} durumunda; bu işlem yapılamaz.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (AppointmentRules.RequiresStarted(to) && appointment.StartAt > now)
        {
            return Problems.Conflict("appointment_not_started", "Randevu saati gelmeden tamamlandı veya gelmedi işaretlenemez.");
        }

        var cancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var userId = member.UserId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Appointments
            .Where(a => a.Id == id && a.Status == from && (from != AppointmentStatus.Completed || a.SaleId == null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, to)
                .SetProperty(a => a.ConfirmedAt, a => to == AppointmentStatus.Confirmed && a.ConfirmedAt == null ? now : a.ConfirmedAt)
                .SetProperty(a => a.CompletedAt, a => to == AppointmentStatus.Completed ? now : reopen ? null : a.CompletedAt)
                .SetProperty(a => a.NoShowAt, a => to == AppointmentStatus.NoShow ? now : reopen ? null : a.NoShowAt)
                .SetProperty(a => a.CancelledAt, a => to == AppointmentStatus.Cancelled ? now : a.CancelledAt)
                .SetProperty(a => a.CancelledBy, a => to == AppointmentStatus.Cancelled ? userId : a.CancelledBy)
                .SetProperty(a => a.CancelSource, a => to == AppointmentStatus.Cancelled ? AppointmentCancelSource.Business : a.CancelSource)
                .SetProperty(a => a.CancelReason, a => to == AppointmentStatus.Cancelled ? cancelReason : a.CancelReason)
                .SetProperty(a => a.PackageSessionUsed, a => to == AppointmentStatus.Completed ? a.CustomerPackageId != null
                    : from == AppointmentStatus.Completed ? false : a.PackageSessionUsed)
                .SetProperty(a => a.UpdatedAt, now), ct);
        if (changed != 1)
        {
            return Problems.Conflict("appointment_changed", "Randevu bu sırada değişti; sayfayı yenileyip tekrar deneyin.");
        }

        // Paket seansı (kilit sırası randevu → paket): Tamamlandı'da atomik +1; seans kalmadıysa veya süre randevu gününden önce
        // bittiyse transaction geri alınır (409). Tamamlandı'nın geri alınması seansı iade eder. Gelmedi ve İptal seans düşmez.
        if (to == AppointmentStatus.Completed && appointment.CustomerPackageId is { } usePackage)
        {
            var startDay = BusinessTime.LocalDay(appointment.StartAt);
            var used = await db.CustomerPackages
                .Where(p => p.Id == usePackage && p.Status == CustomerPackageStatus.Active && p.UsedSessions < p.TotalSessions && p.ExpiresOn >= startDay)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedSessions, p => p.UsedSessions + 1).SetProperty(p => p.UpdatedAt, now), ct);
            if (used != 1)
            {
                return Problems.Conflict("package_exhausted",
                    "Paketin seansı kalmadı, süresi doldu veya paket kapatıldı. Randevudan paketi kaldırın ya da paketin süresini uzatın.");
            }
        }
        else if (from == AppointmentStatus.Completed && appointment.PackageSessionUsed && appointment.CustomerPackageId is { } returnPackage)
        {
            await db.CustomerPackages.Where(p => p.Id == returnPackage && p.UsedSessions > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedSessions, p => p.UsedSessions - 1).SetProperty(p => p.UpdatedAt, now), ct);
        }

        if (to == AppointmentStatus.Completed)
        {
            await ContactVisits.TouchAsync(db, appointment.ContactId, appointment.StartAt, ct);
        }
        else if (from == AppointmentStatus.Completed)
        {
            await ContactVisits.RecomputeAsync(db, appointment.ContactId, ct);
        }

        await tx.CommitAsync(ct);

        AppointmentSmsKind? sms = (from, to) switch
        {
            (AppointmentStatus.Pending, AppointmentStatus.Confirmed) => AppointmentSmsKind.Confirmed,
            (_, AppointmentStatus.Cancelled) when reject => AppointmentSmsKind.Rejected,
            (_, AppointmentStatus.Cancelled) when notify => AppointmentSmsKind.Cancelled,
            _ => null,
        };
        if (sms is { } kind)
        {
            await notifier.SendAsync(id, kind, ct);
        }

        return TypedResults.Ok(await LoadAsync(db, id, ct));
    }

    private static async Task<Results<Ok<AppointmentSaleDraft>, ProblemHttpResult>> SaleDraft(
        Guid id, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var appointment = await db.Appointments.AsNoTracking().Visible(scope).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
        {
            return NotFound();
        }

        if (appointment.SaleId is not null)
        {
            return Problems.Conflict("appointment_already_converted", "Bu randevu zaten satışa çevrildi.");
        }

        if (appointment.Status != AppointmentStatus.Completed)
        {
            return Problems.Conflict("appointment_not_completed", "Satışa çevirmek için önce randevuyu tamamlandı olarak işaretleyin.");
        }

        var contact = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(c => c.Id == appointment.ContactId, ct);
        var service = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(p => p.Id == appointment.ProductId, ct);
        var staff = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(s => s.Id == appointment.StaffMemberId, ct);

        // Paketten kullanılan randevunun hizmet satırı 0,00 TL; açıklamada paket ve seans ("Paket: 10 seans lazer (4/10)").
        if (appointment.PackageSessionUsed && appointment.CustomerPackageId is { } packageId
            && await db.CustomerPackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId, ct) is { } package)
        {
            return TypedResults.Ok(new AppointmentSaleDraft(appointment.Id, contact.Id, contact.DisplayName, service.Id,
                $"Paket: {package.Name} ({package.UsedSessions}/{package.TotalSessions})", 0m, staff.Id, staff.FullName));
        }

        return TypedResults.Ok(new AppointmentSaleDraft(
            appointment.Id, contact.Id, contact.DisplayName, service.Id, service.Name, service.GrossSalePrice, staff.Id, staff.FullName));
    }

    private static async Task<IResult> Calendar(
        DateOnly from, DateOnly to, CurrentMember member, AppDbContext db, CancellationToken ct, Guid? staffId = null)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxCalendarDays)
        {
            return Validation.FieldProblem("to", "Takvim en fazla 14 gün gösterilir.");
        }

        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var myStaffId = scope.Restricted ? scope.StaffId : await StaffAccess.MyStaffIdAsync(db, member, ct);
        var staffQuery = db.StaffMembers.AsNoTracking().Where(s => s.IsActive);
        if (scope.Restricted)
        {
            staffQuery = staffQuery.Where(s => s.Id == scope.StaffId);
        }

        if (staffId is { } only)
        {
            staffQuery = staffQuery.Where(s => s.Id == only);
        }

        var staff = await staffQuery.OrderBy(s => s.SortOrder).ThenBy(s => s.FullName).ToListAsync(ct);
        var staffIds = staff.Select(s => s.Id).ToList();
        var plans = await AvailabilityQueries.LoadAsync(db, staffIds, from, to, includeAppointments: false, null, ct);
        var (startUtc, endUtc) = BusinessTime.RangeUtc(from, to);

        var appointments = db.Appointments.Visible(scope).Where(a => a.StartAt < endUtc && a.EndAt > startUtc);
        if (staffId is { } onlyStaff)
        {
            appointments = appointments.Where(a => a.StaffMemberId == onlyStaff);
        }

        var items = await ProjectAsync(db, appointments.OrderBy(a => a.StartAt), ct);
        var timeOffs = await db.StaffTimeOffs.AsNoTracking()
            .Where(t => staffIds.Contains(t.StaffMemberId) && t.StartAt < endUtc && t.EndAt > startUtc)
            .OrderBy(t => t.StartAt)
            .Select(t => new TimeOffResponse(t.Id, t.StaffMemberId, t.StartAt, t.EndAt, t.Kind, t.Note))
            .ToListAsync(ct);

        var days = new List<CalendarDay>();
        var week = (await db.WorkingHours.AsNoTracking().ToListAsync(ct)).ToDictionary(w => w.Day);
        var holidays = await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to).ToDictionaryAsync(h => h.Date, h => h.Name, ct);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var hours = week.GetValueOrDefault(day.DayOfWeek);
            var closed = hours is null || hours.IsClosed;
            days.Add(new CalendarDay(day, closed, holidays.GetValueOrDefault(day),
                closed ? null : TimeText.Format(hours!.OpensAt), closed ? null : TimeText.Format(hours!.ClosesAt)));
        }

        var columns = staff.Select(s => new CalendarStaff(s.Id, s.FullName, s.Title, s.Color, days
            .Select(d => new CalendarStaffDay(d.Date, Availability.WorkingRanges(plans[(s.Id, d.Date)])
                .Select(r => new TimeRangeDto(Availability.Format(r.Start), Availability.Format(r.End))).ToList()))
            .ToList())).ToList();
        return TypedResults.Ok(new CalendarResponse(from, to, days, columns, items, timeOffs, scope.Restricted, myStaffId));
    }

    private static async Task<IResult> ContactAppointments(Guid id, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Contacts.AnyAsync(c => c.Id == id, ct))
        {
            return Problems.NotFound("Müşteri bulunamadı.");
        }

        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var items = await ProjectAsync(db, db.Appointments.Visible(scope).Where(a => a.ContactId == id).OrderByDescending(a => a.StartAt).Take(200), ct);
        return TypedResults.Ok(new ContactAppointmentsResponse(items, await NoShowAsync(db, id, clock, ct)));
    }

    private static async Task<Ok<List<HolidayResponse>>> ListHolidays(
        AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var start = from ?? BusinessTime.Today(clock);
        var end = to ?? start.AddYears(1);
        return TypedResults.Ok(await db.Holidays.AsNoTracking().Where(h => h.Date >= start && h.Date <= end).OrderBy(h => h.Date)
            .Select(h => new HolidayResponse(h.Id, h.Date, h.Name)).ToListAsync(ct));
    }

    private static async Task<Results<Created<HolidayResponse>, ProblemHttpResult>> CreateHoliday(
        HolidayRequest request, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        if (await db.Holidays.AnyAsync(h => h.Date == request.Date, ct))
        {
            return Problems.Conflict("holiday_exists", $"{request.Date:dd.MM.yyyy} zaten kapalı gün olarak ekli.");
        }

        var holiday = new Holiday { Date = request.Date, Name = request.Name.Trim() };
        db.Holidays.Add(holiday);
        audit.Log(AuditAction.HolidayCreated, nameof(Holiday), holiday.Id, new { holiday.Date, holiday.Name });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/holidays/{holiday.Id}", new HolidayResponse(holiday.Id, holiday.Date, holiday.Name));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteHoliday(Guid id, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (holiday is null)
        {
            return Problems.NotFound("Kapalı gün bulunamadı.");
        }

        db.Holidays.Remove(holiday);
        audit.Log(AuditAction.HolidayDeleted, nameof(Holiday), holiday.Id, new { holiday.Date, holiday.Name });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<AppointmentSettingsResponse>> GetSettings(CurrentMember member, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new AppointmentSettingsResponse(
            await db.Businesses.Where(b => b.Id == member.BusinessId).Select(b => b.StaffSeesOnlyOwnAppointments).FirstAsync(ct)));

    private static async Task<Ok<AppointmentSettingsResponse>> UpdateSettings(
        AppointmentSettingsRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var business = await db.Businesses.SingleAsync(b => b.Id == member.BusinessId, ct);
        if (business.StaffSeesOnlyOwnAppointments != request.StaffSeesOnlyOwn)
        {
            audit.Log(AuditAction.AppointmentSettingsChanged, nameof(Business), business.Id,
                new { staffSeesOnlyOwn = new { from = business.StaffSeesOnlyOwnAppointments, to = request.StaffSeesOnlyOwn } });
            business.StaffSeesOnlyOwnAppointments = request.StaffSeesOnlyOwn;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new AppointmentSettingsResponse(business.StaffSeesOnlyOwnAppointments));
    }
}
