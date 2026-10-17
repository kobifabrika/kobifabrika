using FluentValidation;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Appointments;

/// <summary>
/// Online randevu ve hatırlatma ayarları. BookableStaff: aktif ve "online randevuda görünsün" açık personel sayısı;
/// BookableServices: online sayfada çıkacak hizmet sayısı (aktif, süresi tanımlı, en az bir uygun personeli olan).
/// </summary>
public sealed record BookingSettingsResponse(
    bool Enabled, bool AutoConfirm, int MinNoticeMinutes, int MaxDaysAhead, int SlotMinutes, int ReminderHoursBefore, int CustomerCancelHours,
    string Slug, int BookableStaff, int BookableServices);

public sealed record BookingSettingsRequest(
    bool Enabled, bool AutoConfirm, int MinNoticeMinutes, int MaxDaysAhead, int SlotMinutes, int ReminderHoursBefore, int CustomerCancelHours);

public sealed class BookingSettingsRequestValidator : AbstractValidator<BookingSettingsRequest>
{
    public BookingSettingsRequestValidator()
    {
        RuleFor(x => x.MinNoticeMinutes).InclusiveBetween(0, Business.MaxBookingMinNoticeMinutes)
            .WithMessage("En erken randevu 0 dakika ile 7 gün arasında olmalı.");
        RuleFor(x => x.MaxDaysAhead).InclusiveBetween(1, Business.MaxBookingDaysAhead).WithMessage("En fazla gün 1 ile 180 arasında olmalı.");
        RuleFor(x => x.SlotMinutes).Must(m => Business.BookingSlotOptions.Contains(m))
            .WithMessage("Saat aralığı 5, 10, 15, 20, 30 veya 60 dakika olmalı.");
        RuleFor(x => x.ReminderHoursBefore).InclusiveBetween(0, Business.MaxReminderHoursBefore)
            .WithMessage("Hatırlatma 0 (kapalı) ile 72 saat arasında olmalı.");
        RuleFor(x => x.CustomerCancelHours).InclusiveBetween(0, Business.MaxCustomerCancelHours)
            .WithMessage("Müşteri iptal süresi 0 (kapalı) ile 72 saat arasında olmalı.");
    }
}

public static class BookingSettingsEndpoints
{
    public static IEndpointRouteBuilder MapBookingSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/business/booking-settings", Get).WithTags("Business").RequireAuthorization(Policies.ViewSettings)
            .WithSummary("Online randevu ve hatırlatma ayarları");
        app.MapPut("/api/business/booking-settings", Update).WithTags("Business").RequireAuthorization(Policies.ManageBusiness)
            .Validate<BookingSettingsRequest>()
            .WithSummary("Online randevu ve hatırlatma ayarları (açmak için en az bir online personel gerekir)");
        return app;
    }

    private static async Task<BookingSettingsResponse> ResponseAsync(AppDbContext db, Business b, CancellationToken ct)
    {
        var (staff, services) = await PublicBookingQueries.CountsAsync(db, ct);
        return new BookingSettingsResponse(b.BookingEnabled, b.BookingAutoConfirm, b.BookingMinNoticeMinutes, b.BookingMaxDaysAhead,
            b.BookingSlotMinutes, b.ReminderHoursBefore, b.CustomerCancelHours, b.Slug, staff, services);
    }

    private static async Task<Ok<BookingSettingsResponse>> Get(CurrentMember member, AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ResponseAsync(db, await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == member.BusinessId, ct), ct));

    private static async Task<Results<Ok<BookingSettingsResponse>, ProblemHttpResult>> Update(
        BookingSettingsRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, IPlanAccess plans, CancellationToken ct)
    {
        var business = await db.Businesses.SingleAsync(b => b.Id == member.BusinessId, ct);
        // Paket online randevuyu kapsamıyorsa sayfa açılamaz; açık kalmışsa kapatılabilir ve diğer ayarlar değişebilir.
        if (request.Enabled && await plans.FeatureBlockedMessageAsync(PlanFeatures.OnlineBooking, ct) is { } blocked)
        {
            return Problems.PlanFeature(blocked);
        }

        if (request.Enabled && !business.BookingEnabled && (await PublicBookingQueries.CountsAsync(db, ct)).Staff == 0)
        {
            return Problems.Conflict("booking_no_staff",
                "Online randevuyu açmak için en az bir aktif personelde \"Online randevuda görünsün\" açık olmalı.");
        }

        var before = new
        {
            enabled = business.BookingEnabled, autoConfirm = business.BookingAutoConfirm, minNotice = business.BookingMinNoticeMinutes,
            maxDays = business.BookingMaxDaysAhead, slot = business.BookingSlotMinutes, reminder = business.ReminderHoursBefore,
            cancelHours = business.CustomerCancelHours,
        };
        business.BookingEnabled = request.Enabled;
        business.BookingAutoConfirm = request.AutoConfirm;
        business.BookingMinNoticeMinutes = request.MinNoticeMinutes;
        business.BookingMaxDaysAhead = request.MaxDaysAhead;
        business.BookingSlotMinutes = request.SlotMinutes;
        business.ReminderHoursBefore = request.ReminderHoursBefore;
        business.CustomerCancelHours = request.CustomerCancelHours;
        if (db.Entry(business).State == EntityState.Modified)
        {
            audit.Log(AuditAction.BookingSettingsChanged, nameof(Business), business.Id, new { before, after = request });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(await ResponseAsync(db, business, ct));
    }
}
