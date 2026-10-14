using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Appointments;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Public;

/// <summary>Price: KDV dahil; 0 TL hizmette null (fiyat yazmaz).</summary>
public sealed record PublicBookingService(Guid Id, string Name, int DurationMinutes, decimal? Price);

public sealed record PublicBookingStaff(Guid Id, string FullName, string? Title, IReadOnlyList<Guid> ServiceIds);

/// <summary>Online randevu sayfası. Phone gösterim biçiminde (0212 555 44 33), PhoneE164 tel: linki için. Today: İstanbul günü.</summary>
public sealed record PublicBookingResponse(
    string Slug, string Name, string? LogoUrl, string? Address, string? City, string? District, string? Phone, string? PhoneE164,
    IReadOnlyList<PublicBookingService> Services, IReadOnlyList<PublicBookingStaff> Staff, DateOnly Today, DateOnly LastDay, int SlotMinutes,
    bool AutoConfirm, int CustomerCancelHours, string KvkkVersion);

/// <summary>Gün → boş başlangıç saatleri ("HH:mm", İstanbul).</summary>
public sealed record PublicSlotDay(DateOnly Date, IReadOnlyList<string> Times);

/// <summary>LastDay: online randevu alınabilecek son gün.</summary>
public sealed record PublicSlotsResponse(IReadOnlyList<PublicSlotDay> Days, DateOnly LastDay);

public sealed record VerificationRequest(string? Phone, string? FirstName);

public sealed record VerificationResponse(Guid VerificationId, int ExpiresInSeconds, int ResendAfterSeconds);

/// <summary>StaffId boşsa "Fark etmez": o saatte boş, o gün en az dolu personel.</summary>
public sealed record PublicBookingRequest(
    Guid VerificationId, string? Code, Guid ServiceId, Guid? StaffId, DateTime StartAt, string? FirstName, string? LastName, string? Phone,
    bool MarketingConsent);

/// <summary>ManageToken: müşterinin randevu sayfası /r/[slug]/randevu/[token] (onay ekranındaki iptal linki).</summary>
public sealed record PublicBookingCreated(
    string ManageToken, AppointmentStatus Status, DateTime StartAt, DateTime EndAt, string StaffName, string ServiceName);

/// <summary>Müşterinin randevu sayfası. CancelDeadline: bu andan sonra online iptal yok (CustomerCancelHours = 0 ise null).</summary>
public sealed record PublicAppointmentResponse(
    string BusinessName, string Slug, string? LogoUrl, string? BusinessPhone, string? BusinessPhoneE164, string? Address, string ContactName,
    string ServiceName, string StaffName, DateTime StartAt, DateTime EndAt, AppointmentStatus Status, bool CanCancel, DateTime? CancelDeadline);

public sealed class VerificationRequestValidator : AbstractValidator<VerificationRequest>
{
    public VerificationRequestValidator()
    {
        RuleFor(x => x.Phone).Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage("Cep telefonu numaranızı girin.");
        RuleFor(x => x.FirstName).MaximumLength(100).WithMessage("Ad en fazla 100 karakter olabilir.");
    }
}

public sealed class PublicBookingRequestValidator : AbstractValidator<PublicBookingRequest>
{
    public PublicBookingRequestValidator()
    {
        RuleFor(x => x.Code).Must(c => c is { Length: PhoneVerification.CodeLength } && c.All(char.IsAsciiDigit))
            .WithMessage("SMS ile gelen 6 haneli kodu girin.");
        RuleFor(x => x.ServiceId).NotEmpty().WithMessage("Hizmet seçin.");
        RuleFor(x => x.StartAt).Must(AppointmentTimes.WholeMinute).WithMessage("Saat seçin.");
        RuleFor(x => x.FirstName).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Adınızı girin.")
            .MaximumLength(100).WithMessage("Ad en fazla 100 karakter olabilir.");
        RuleFor(x => x.LastName).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Soyadınızı girin.")
            .MaximumLength(100).WithMessage("Soyad en fazla 100 karakter olabilir.");
        RuleFor(x => x.Phone).Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage("Cep telefonu numaranızı girin.");
    }
}

/// <summary>
/// Online randevu (/r/[slug]) uçları; anonim. İşletme slug'dan çözülür ve işlem o işletmenin kiracı scope'unda yapılır. İşletme yoksa,
/// Randevular modülü kapalıysa veya online randevu kapalıysa 404. Hız sınırları IP'ye dayanır (canlıda forwarded headers Sprint 8).
/// Doğrulama kodu: IP başına 10 dakikada 5 (booking-sms), numara başına 60 sn'de 1 ve saatte 3 (bütün işletmeler), işletme başına
/// günlük üst sınır (Sms:VerificationDailyLimit).
/// </summary>
public static class PublicBookingEndpoints
{
    public const string ReadRateLimit = "booking-read";
    public const string SmsRateLimit = "booking-sms";

    public static IEndpointRouteBuilder MapPublicBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var booking = app.MapGroup("/api/public/booking/{slug}").WithTags("Public").AllowAnonymous()
            .AddEndpointFilter(PublicLinkEndpoints.PrivateHeaders);
        booking.MapGet("/", Info).RequireRateLimiting(ReadRateLimit).Produces<PublicBookingResponse>().WithSummary("Online randevu sayfası: işletme, hizmetler, personeller");
        booking.MapGet("/slots", Slots).RequireRateLimiting(ReadRateLimit).Produces<PublicSlotsResponse>()
            .WithSummary("Boş saatler (serviceId, staffId boşsa \"Fark etmez\", from, days ≤ 14)");
        booking.MapPost("/verifications", SendCode).RequireRateLimiting(SmsRateLimit).Validate<VerificationRequest>()
            .Produces<VerificationResponse>()
            .WithSummary("Telefona doğrulama kodu gönder");
        booking.MapPost("/appointments", Book).RequireRateLimiting(PublicLinkEndpoints.RateLimit).Validate<PublicBookingRequest>()
            .Produces<PublicBookingCreated>(StatusCodes.Status201Created)
            .WithSummary("Kodu doğrula ve randevuyu oluştur (saat dolduysa 409 slot_taken)");

        var manage = app.MapGroup("/api/public/appointments/{token}").WithTags("Public").AllowAnonymous()
            .RequireRateLimiting(PublicLinkEndpoints.RateLimit).AddEndpointFilter(PublicLinkEndpoints.PrivateHeaders);
        manage.MapGet("/", ManagePage).Produces<PublicAppointmentResponse>().WithSummary("Müşterinin randevu sayfası");
        manage.MapPost("/cancel", CustomerCancel).Produces<PublicAppointmentResponse>().WithSummary("Müşteri randevuyu iptal eder (en az X saat kala)");
        return app;
    }

    private static ProblemHttpResult PageNotFound() => Problems.NotFound("Online randevu sayfası bulunamadı.");

    private static ProblemHttpResult ServiceNotFound() => Problems.NotFound("Hizmet bulunamadı.");

    /// <summary>Kurulumu bitmiş, Randevular modülü ve online randevusu açık işletme.</summary>
    private static async Task<Business?> FindAsync(AppDbContext db, string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 100)
        {
            return null;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var business = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Slug == normalized && b.SetupCompletedAt != null, ct);
        return business is { BookingEnabled: true } && business.Modules.HasFlag(BusinessModules.Appointments) ? business : null;
    }

    private static string? FormatPhone(string? e164) => e164 is null ? null : AppointmentNotifier.FormatPhone(e164);

    private static async Task<IResult> Info(string slug, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        if (await FindAsync(db, slug, ct) is not { } business)
        {
            return PageNotFound();
        }

        await using var scope = scopes.TenantScope(business.Id);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (services, staff) = await PublicBookingQueries.LoadAsync(tenantDb, ct);
        var serviceIds = services.Select(s => s.Id).ToList();
        var today = BusinessTime.Today(clock);
        return TypedResults.Ok(new PublicBookingResponse(
            business.Slug, business.Name, business.LogoUrl, business.Address, business.City, business.District, FormatPhone(business.Phone), business.Phone,
            services.Select(s => new PublicBookingService(s.Id, s.Name, s.DurationMinutes!.Value, s.GrossSalePrice > 0 ? s.GrossSalePrice : null)).ToList(),
            staff.Select(s => new PublicBookingStaff(s.Id, s.FullName, s.Title, serviceIds.Where(s.Gives).ToList()))
                .Where(s => s.ServiceIds.Count > 0).ToList(),
            today, BookingWindow.LastDay(today, business.BookingMaxDaysAhead), business.BookingSlotMinutes, business.BookingAutoConfirm,
            business.CustomerCancelHours, KvkkNotice.Version));
    }

    private static async Task<IResult> Slots(
        string slug, Guid serviceId, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct,
        Guid? staffId = null, DateOnly? from = null, int days = 7)
    {
        if (await FindAsync(db, slug, ct) is not { } business)
        {
            return PageNotFound();
        }

        if (days is < 1 or > 14)
        {
            return Validation.FieldProblem("days", "En fazla 14 gün istenebilir.");
        }

        await using var scope = scopes.TenantScope(business.Id);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (services, staff) = await PublicBookingQueries.LoadAsync(tenantDb, ct);
        if (services.FirstOrDefault(s => s.Id == serviceId) is not { } service)
        {
            return ServiceNotFound();
        }

        var candidates = staff.Where(s => s.Gives(serviceId) && (staffId is null || s.Id == staffId)).ToList();
        if (staffId is not null && candidates.Count == 0)
        {
            return Problems.NotFound("Personel bulunamadı.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var today = BusinessTime.LocalDay(now);
        var start = from ?? today;
        var end = start.AddDays(days - 1);
        var slots = await PublicBookingQueries.SlotsAsync(tenantDb, business, service, candidates, start, end, now, ct);
        var result = new List<PublicSlotDay>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var times = slots.TryGetValue(day, out var list)
                ? Availability.Union(list.GroupBy(x => x.StaffId).Select(g => (IReadOnlyList<DateTime>)g.Select(x => x.Start).ToList()))
                    .Select(t => BusinessTime.ToLocal(t).ToString("HH:mm")).ToList()
                : [];
            result.Add(new PublicSlotDay(day, times));
        }

        return TypedResults.Ok(new PublicSlotsResponse(result, BookingWindow.LastDay(today, business.BookingMaxDaysAhead)));
    }

    private static async Task<IResult> SendCode(
        string slug, VerificationRequest request, HttpContext http, AppDbContext db, IServiceScopeFactory scopes, IOptions<SmsOptions> sms,
        TimeProvider clock, CancellationToken ct)
    {
        if (await FindAsync(db, slug, ct) is not { } business)
        {
            return PageNotFound();
        }

        if (!PhoneNumber.TryNormalize(request.Phone, out var phone) || !PhoneVerification.IsMobile(phone))
        {
            return Validation.FieldProblem("phone", "Cep telefonu numaranızı girin (ör. 0532 123 45 67).");
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // Numara başına sınır bütün işletmelerde sayılır: aynı numaraya farklı işletmelerden SMS bombası atılmasın.
        var hourAgo = now.AddHours(-1);
        var recent = await db.PhoneVerifications.IgnoreQueryFilters().Where(v => v.Phone == phone && v.CreatedAt > hourAgo)
            .Select(v => v.CreatedAt).ToListAsync(ct);
        if (recent.Count > 0 && recent.Max() > now - PhoneVerification.ResendAfter)
        {
            var wait = (int)Math.Ceiling((recent.Max() + PhoneVerification.ResendAfter - now).TotalSeconds);
            return Problems.TooManyRequests("code_too_soon", $"Yeni kod için {wait} saniye bekleyin.",
                new Dictionary<string, object?> { ["retryAfterSeconds"] = wait });
        }

        if (recent.Count >= PhoneVerification.HourlyPerPhone)
        {
            return Problems.TooManyRequests("code_hourly_limit", "Bu numaraya çok fazla kod gönderildi. 1 saat sonra tekrar deneyin.");
        }

        var dayStart = BusinessTime.StartOfDayUtc(BusinessTime.LocalDay(now));
        var sentToday = await db.MessageLogs.IgnoreQueryFilters()
            .CountAsync(m => m.BusinessId == business.Id && m.Purpose == MessagePurpose.Verification && m.CreatedAt >= dayStart, ct);
        if (sentToday >= sms.Value.VerificationDailyLimit)
        {
            return Problems.TooManyRequests("booking_sms_limit",
                business.Phone is { } p
                    ? $"Şu anda online randevu alınamıyor. Randevu için işletmeyi arayın: {FormatPhone(p)}"
                    : "Şu anda online randevu alınamıyor. Randevu için işletmeyi arayın.",
                new Dictionary<string, object?> { ["businessPhone"] = business.Phone });
        }

        await using var scope = scopes.TenantScope(business.Id);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = PhoneVerification.NewCode();
        var verification = new PhoneVerification
        {
            Phone = phone,
            CodeHash = [],
            ExpiresAt = now + PhoneVerification.Lifetime,
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
        };
        verification.CodeHash = PhoneVerification.Hash(verification.Id, code);
        tenantDb.PhoneVerifications.Add(verification);
        await tenantDb.SaveChangesAsync(ct);

        var log = await scope.ServiceProvider.GetRequiredService<ISmsSender>().SendAsync(
            new SmsMessage(phone, SmsTexts.Verification(business.Name, code), MessagePurpose.Verification, null, nameof(PhoneVerification), verification.Id),
            ct);
        if (log.Status == MessageStatus.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "SMS gönderilemedi. Biraz sonra tekrar deneyin.",
                extensions: new Dictionary<string, object?> { ["code"] = "sms_failed" });
        }

        return TypedResults.Ok(new VerificationResponse(verification.Id, (int)PhoneVerification.Lifetime.TotalSeconds,
            (int)PhoneVerification.ResendAfter.TotalSeconds));
    }

    private static ProblemHttpResult CodeInvalid(int remaining) => Problems.BadRequest("code_invalid",
        remaining > 0 ? $"Kod hatalı. {remaining} deneme hakkınız kaldı." : "Kod hatalı. Deneme hakkınız bitti; yeni kod isteyin.",
        new Dictionary<string, object?> { ["remainingAttempts"] = remaining });

    /// <summary>
    /// Kodu doğrular: süresi geçmişse 410, 3 yanlış denemeden sonra 429, yanlışsa deneme sayısı ayrı bir koşullu UPDATE ile artar (400).
    /// Doğru kod bir kez doğrulanmış sayılır; randevuya dönüşmediyse (ör. saat doldu) 15 dakika içinde başka saatle kullanılabilir.
    /// </summary>
    private static async Task<IResult?> VerifyAsync(AppDbContext db, PhoneVerification? v, string phone, string code, DateTime now, CancellationToken ct)
    {
        if (v is null || v.Phone != phone)
        {
            return CodeInvalid(0);
        }

        if (v.ConsumedAt is not null)
        {
            return Problems.Gone("code_used", "Bu kod kullanıldı. Yeni kod isteyin.");
        }

        if (v.VerifiedAt is { } verified ? verified + PhoneVerification.VerifiedReuse <= now : v.ExpiresAt <= now)
        {
            return Problems.Gone("code_expired", "Kodun süresi doldu. Yeni kod isteyin.");
        }

        if (v.Attempts >= PhoneVerification.MaxAttempts)
        {
            return Problems.TooManyRequests("code_attempts", "Çok fazla yanlış deneme. Yeni kod isteyin.");
        }

        if (!v.Matches(code))
        {
            await db.PhoneVerifications.Where(x => x.Id == v.Id && x.Attempts < PhoneVerification.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
            var attempts = await db.PhoneVerifications.Where(x => x.Id == v.Id).Select(x => x.Attempts).FirstAsync(ct);
            return CodeInvalid(PhoneVerification.MaxAttempts - attempts);
        }

        if (v.VerifiedAt is null)
        {
            await db.PhoneVerifications.Where(x => x.Id == v.Id && x.VerifiedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.VerifiedAt, now), ct);
        }

        return null;
    }

    private static async Task<IResult> Book(
        string slug, PublicBookingRequest request, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        if (await FindAsync(db, slug, ct) is not { } business)
        {
            return PageNotFound();
        }

        if (!PhoneNumber.TryNormalize(request.Phone, out var phone) || !PhoneVerification.IsMobile(phone))
        {
            return Validation.FieldProblem("phone", "Cep telefonu numaranızı girin (ör. 0532 123 45 67).");
        }

        await using var scope = scopes.TenantScope(business.Id);
        var sp = scope.ServiceProvider;
        var tenantDb = sp.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var verification = await tenantDb.PhoneVerifications.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VerificationId, ct);
        if (await VerifyAsync(tenantDb, verification, phone, request.Code!, now, ct) is { } codeProblem)
        {
            return codeProblem;
        }

        var (services, staff) = await PublicBookingQueries.LoadAsync(tenantDb, ct);
        if (services.FirstOrDefault(s => s.Id == request.ServiceId) is not { } service)
        {
            return Validation.FieldProblem("serviceId", "Hizmet bulunamadı.");
        }

        var eligible = staff.Where(s => s.Gives(service.Id) && (request.StaffId is null || s.Id == request.StaffId)).ToList();
        if (request.StaffId is not null && eligible.Count == 0)
        {
            return Validation.FieldProblem("staffId", "Personel bulunamadı.");
        }

        var start = AppointmentTimes.Utc(request.StartAt);
        var day = BusinessTime.LocalDay(start);
        var free = (await PublicBookingQueries.SlotsAsync(tenantDb, business, service, eligible, day, day, now, ct)).GetValueOrDefault(day) ?? [];
        var candidates = free.Where(x => x.Start == start).Select(x => x.StaffId).ToList();
        if (candidates.Count == 0)
        {
            var possible = (await PublicBookingQueries.SlotsAsync(tenantDb, business, service, eligible, day, day, now, ct, includeAppointments: false))
                .GetValueOrDefault(day) ?? [];
            return possible.Any(x => x.Start == start) ? SlotTaken() : Problems.BadRequest("slot_invalid", "Bu saat için online randevu alınamaz; başka bir saat seçin.");
        }

        var booked = await PublicBookingQueries.BookedMinutesAsync(tenantDb, candidates, day, ct);
        var ordered = eligible.Where(s => candidates.Contains(s.Id))
            .OrderBy(s => booked.GetValueOrDefault(s.Id)).ThenBy(s => s.SortOrder).ThenBy(s => s.FullName).ToList();

        var ledger = sp.GetRequiredService<LedgerService>();
        var bookingService = sp.GetRequiredService<AppointmentBooking>();
        var protector = sp.GetRequiredService<LinkTokenProtector>();
        var end = start.AddMinutes(service.DurationMinutes!.Value);
        var status = business.BookingAutoConfirm ? AppointmentStatus.Confirmed : AppointmentStatus.Pending;
        Appointment? created = null;
        string? token = null;
        BookableStaff? chosen = null;

        // Her aday ayrı transaction'da denenir (kilitli personel bir sonraki adaya geçmeden bırakılır). Aynı numarayla eşzamanlı
        // ilk müşteri kaydında benzersiz indeks hatası olursa bir kez daha denenir (bu kez var olan müşteri bulunur).
        foreach (var candidate in ordered.Concat(ordered.Take(1)))
        {
            try
            {
                (created, token) = await ledger.InTransactionAsync(async () =>
                {
                    var consumed = await tenantDb.PhoneVerifications.Where(v => v.Id == request.VerificationId && v.ConsumedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(v => v.ConsumedAt, now), ct);
                    if (consumed != 1)
                    {
                        throw new ConflictException("code_used", "Bu kod kullanıldı. Yeni kod isteyin.");
                    }

                    var contact = await FindOrCreateContactAsync(tenantDb, request, phone, now, ct);
                    var (newToken, hash) = SecureTokens.Create();
                    var appointment = new Appointment
                    {
                        ContactId = contact.Id,
                        StaffMemberId = candidate.Id,
                        ProductId = service.Id,
                        StartAt = start,
                        EndAt = end,
                        Status = status,
                        Source = AppointmentSource.Online,
                        ConfirmedAt = status == AppointmentStatus.Confirmed ? now : null,
                        TimeSetAt = now,
                        ManageTokenHash = hash,
                        ManageToken = protector.Protect(newToken),
                    };
                    tenantDb.Appointments.Add(appointment);
                    await bookingService.SaveAsync(appointment, ct);
                    await tenantDb.PhoneVerifications.Where(v => v.Id == request.VerificationId)
                        .ExecuteUpdateAsync(s => s.SetProperty(v => v.AppointmentId, appointment.Id), ct);
                    return (appointment, newToken);
                }, ct);
                chosen = candidate;
                break;
            }
            catch (ConflictException ex) when (ex.Code is "appointment_overlap" or "staff_inactive")
            {
                tenantDb.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                tenantDb.ChangeTracker.Clear();
            }
        }

        if (created is null || chosen is null)
        {
            return SlotTaken();
        }

        var notifications = sp.GetRequiredService<INotificationService>();
        var contactName = await tenantDb.Contacts.Where(c => c.Id == created.ContactId).Select(c => c.DisplayName).FirstAsync(ct);
        var title = status == AppointmentStatus.Pending ? $"Onay bekleyen online randevu: {contactName}" : $"Online randevu: {contactName}";
        foreach (var userId in await PublicBookingQueries.RecipientsAsync(tenantDb, business.Id, chosen.Id, ct))
        {
            notifications.Notify(userId,
                status == AppointmentStatus.Pending ? NotificationTypes.AppointmentOnlinePending : NotificationTypes.AppointmentOnlineCreated,
                title, $"{SmsTexts.When(start)} · {service.Name} · {chosen.FullName}", PublicBookingQueries.CalendarLink(created.Id, start));
        }

        await tenantDb.SaveChangesAsync(ct);
        await sp.GetRequiredService<AppointmentNotifier>().SendAsync(created.Id, AppointmentSmsKind.Booked, ct);

        return TypedResults.Created($"/api/public/appointments/{token}",
            new PublicBookingCreated(token!, status, start, end, chosen.FullName, service.Name));
    }

    private static ProblemHttpResult SlotTaken() => Problems.Conflict("slot_taken", "Bu saat az önce doldu. Başka bir saat seçin.");

    /// <summary>
    /// Telefonla eşleşen müşteri kullanılır (adı değiştirilmez; arşivdeki de). Silinmiş (KVKK ile silinmiş/anonimleştirilmiş) kayda asla
    /// bağlanmaz: kiracı ve soft delete filtresi dışında kalır, yeni müşteri açılır. Pazarlama izni işaretliyse SMS izni yazılır.
    /// </summary>
    private static async Task<Contact> FindOrCreateContactAsync(AppDbContext db, PublicBookingRequest request, string phone, DateTime now, CancellationToken ct)
    {
        // Anonimleştirilmiş kaydın telefonu zaten silinir; açık şart ikinci güvence (Sprint 6A, KVKK).
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Phone == phone && !c.IsAnonymized, ct);
        if (contact is null)
        {
            contact = new Contact
            {
                FirstName = request.FirstName!.Trim(),
                LastName = request.LastName!.Trim(),
                Phone = phone,
                IsCustomer = true,
                Source = ContactSource.OnlineBooking,
            };
            contact.RefreshDisplayName();
            db.Contacts.Add(contact);
        }
        else if (!contact.IsCustomer)
        {
            contact.IsCustomer = true;
        }

        if (request.MarketingConsent && !contact.SmsConsent)
        {
            contact.SmsConsent = true;
            db.ContactConsents.Add(new ContactConsent
            {
                ContactId = contact.Id, Channel = ConsentChannel.Sms, Granted = true, Source = ConsentSource.OnlineBooking, At = now,
                IysStatus = IysStatuses.Pending,
            });
        }

        await db.SaveChangesAsync(ct);
        return contact;
    }

    private static async Task<(Appointment Appointment, Business Business)?> FindByTokenAsync(AppDbContext db, string token, CancellationToken ct)
    {
        if (!SecureTokens.LooksValid(token))
        {
            return null;
        }

        var hash = SecureTokens.Hash(token);
        var appointment = await db.Appointments.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .FirstOrDefaultAsync(a => a.ManageTokenHash == hash, ct);
        if (appointment is null)
        {
            return null;
        }

        var business = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == appointment.BusinessId, ct);
        return business is null ? null : (appointment, business);
    }

    private static DateTime? CancelDeadline(Appointment a, Business b) =>
        b.CustomerCancelHours > 0 ? a.StartAt.AddHours(-b.CustomerCancelHours) : null;

    private static bool CanCancel(Appointment a, Business b, DateTime now) =>
        a.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed && CancelDeadline(a, b) is { } deadline && now < deadline;

    private static async Task<PublicAppointmentResponse> AppointmentResponseAsync(
        AppDbContext tenantDb, Appointment a, Business b, DateTime now, CancellationToken ct)
    {
        var names = await tenantDb.Appointments.AsNoTracking().Where(x => x.Id == a.Id).Select(x => new
        {
            Contact = tenantDb.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == x.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
            Service = tenantDb.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(p => p.Id == x.ProductId).Select(p => p.Name).FirstOrDefault(),
            Staff = tenantDb.StaffMembers.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(s => s.Id == x.StaffMemberId).Select(s => s.FullName).FirstOrDefault(),
        }).FirstAsync(ct);
        var address = string.Join(", ", new[] { b.Address, b.District, b.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new PublicAppointmentResponse(
            b.Name, b.Slug, b.LogoUrl, FormatPhone(b.Phone), b.Phone, address.Length > 0 ? address : null, names.Contact ?? "", names.Service ?? "",
            names.Staff ?? "", a.StartAt, a.EndAt, a.Status, CanCancel(a, b, now), CancelDeadline(a, b));
    }

    private static async Task<IResult> ManagePage(
        string token, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        if (await FindByTokenAsync(db, token, ct) is not { } found)
        {
            return Problems.NotFound("Randevu bulunamadı. Linki kontrol edin.");
        }

        var (appointment, business) = found;

        await using var scope = scopes.TenantScope(business.Id);
        return TypedResults.Ok(await AppointmentResponseAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), appointment, business, clock.GetUtcNow().UtcDateTime, ct));
    }

    private static async Task<IResult> CustomerCancel(
        string token, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        if (await FindByTokenAsync(db, token, ct) is not { } found)
        {
            return Problems.NotFound("Randevu bulunamadı. Linki kontrol edin.");
        }

        var (appointment, business) = found;

        var now = clock.GetUtcNow().UtcDateTime;
        if (appointment.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
        {
            return Problems.Conflict("invalid_status", appointment.Status == AppointmentStatus.Cancelled
                ? "Bu randevu zaten iptal edildi."
                : "Bu randevu artık iptal edilemez.");
        }

        if (!CanCancel(appointment, business, now))
        {
            var phone = FormatPhone(business.Phone);
            return Problems.Conflict("cancel_too_late", phone is null
                ? "Randevuya az süre kaldığı için online iptal edilemez. İptal için işletmeyi arayın."
                : $"Randevuya az süre kaldığı için online iptal edilemez. İptal için işletmeyi arayın: {phone}");
        }

        await using var scope = scopes.TenantScope(business.Id);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var from = appointment.Status;
        var changed = await tenantDb.Appointments
            .Where(a => a.Id == appointment.Id && a.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AppointmentStatus.Cancelled)
                .SetProperty(a => a.CancelledAt, now)
                .SetProperty(a => a.CancelSource, AppointmentCancelSource.Customer)
                .SetProperty(a => a.CancelReason, "Müşteri online iptal etti")
                .SetProperty(a => a.UpdatedAt, now), ct);
        if (changed != 1)
        {
            return Problems.Conflict("appointment_changed", "Randevu bu sırada değişti; sayfayı yenileyin.");
        }

        var current = await tenantDb.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointment.Id, ct);
        var response = await AppointmentResponseAsync(tenantDb, current, business, now, ct);
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        foreach (var userId in await PublicBookingQueries.RecipientsAsync(tenantDb, business.Id, current.StaffMemberId, ct))
        {
            notifications.Notify(userId, NotificationTypes.AppointmentCustomerCancelled, $"Müşteri randevuyu iptal etti: {response.ContactName}",
                $"{SmsTexts.When(current.StartAt)} · {response.ServiceName} · {response.StaffName}",
                PublicBookingQueries.CalendarLink(current.Id, current.StartAt));
        }

        await tenantDb.SaveChangesAsync(ct);
        return TypedResults.Ok(response);
    }
}
