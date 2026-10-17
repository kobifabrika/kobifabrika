using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Marketing;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Appointments;

public sealed record WhatsAppMessageRequest(WhatsAppNoticeKind Kind);

public sealed record WhatsAppMessageResponse(string Text, string Url);

/// <summary>Yarının randevusu: WhatsApp hatırlatma linki (cep telefonu yoksa null) ve son WhatsApp hatırlatması.</summary>
public sealed record TomorrowNotice(
    Guid Id, DateTime StartAt, DateTime EndAt, AppointmentStatus Status, string ContactName, string ServiceName, string StaffName,
    string? WhatsappUrl, DateTime? RemindedAt);

public sealed record TomorrowNotices(DateOnly Date, bool SmsReminders, IReadOnlyList<TomorrowNotice> Items);

public sealed record SurveyLinkResponse(string Text, string Url, string SurveyUrl);

/// <summary>
/// SMS'siz pilot (karar 2026-10-17): panelden WhatsApp ile müşteriye bildirim. Mesaj POST ile hazırlanır (iptal linki token'ı
/// üretilebilir; GET kayıt oluşturmaz kuralı), düğme gerçek <c>&lt;a href&gt;</c> olur (iPhone Safari); dokunma kaydı keepalive
/// ile <c>whatsapp-notices</c>. "Yarının randevuları" listesi Bugün ve takvimde. Anket linki tamamlanan randevudan (Usta).
/// </summary>
public static class AppointmentNoticeEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentNoticeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments").WithTags("Appointments").RequireAuthorization(Policies.Panel)
            .AddEndpointFilter<AppointmentModuleFilter>();
        group.MapPost("/tomorrow-notices", Tomorrow).Produces<TomorrowNotices>()
            .WithSummary("Yarının randevuları ve WhatsApp hatırlatma linkleri (POST: iptal linki üretilebilir)");
        group.MapPost("/{id:guid}/whatsapp-message", Message).RequireAuthorization(Policies.CanWrite)
            .Produces<WhatsAppMessageResponse>().WithSummary("WhatsApp'ta açılacak hazır mesaj (onay, hatırlatma, taşıma, iptal, ret)");
        group.MapPost("/{id:guid}/whatsapp-notices", Record).RequireAuthorization(Policies.CanWrite)
            .WithSummary("WhatsApp düğmesine dokunuldu (Hatırlatıldı işareti, zaman çizelgesi)");
        group.MapPost("/{id:guid}/survey-link", SurveyLink).RequireAuthorization(Policies.CanWrite).RequireFeature(PlanFeatures.Surveys)
            .Produces<SurveyLinkResponse>().WithSummary("Tamamlanan randevuya anket linki (WhatsApp mesajıyla)");
        return app;
    }

    private static async Task<Ok<TomorrowNotices>> Tomorrow(
        CurrentMember member, AppDbContext db, AppointmentNotifier notifier, TimeProvider clock, CancellationToken ct)
    {
        var tomorrow = BusinessTime.Today(clock).AddDays(1);
        var (start, end) = BusinessTime.DayRangeUtc(tomorrow);
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var query = db.Appointments.AsNoTracking()
            .Where(a => a.StartAt >= start && a.StartAt < end && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending));
        if (scope.Restricted)
        {
            query = query.Where(a => a.StaffMemberId == scope.StaffId);
        }

        var rows = await query.OrderBy(a => a.StartAt)
            .Select(a => new
            {
                a.Id, a.StartAt, a.EndAt, a.Status,
                Contact = db.Contacts.IgnoreQueryFilters(SoftDeleteOnly).Where(c => c.Id == a.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                Service = db.Products.IgnoreQueryFilters(SoftDeleteOnly).Where(p => p.Id == a.ProductId).Select(p => p.Name).FirstOrDefault(),
                Staff = db.StaffMembers.IgnoreQueryFilters(SoftDeleteOnly).Where(s => s.Id == a.StaffMemberId).Select(s => s.FullName).FirstOrDefault(),
                RemindedAt = db.MessageLogs.Where(m => m.RefType == "Appointment" && m.RefId == a.Id && m.Channel == MessageChannel.WhatsApp
                    && m.Purpose == MessagePurpose.AppointmentReminder).Max(m => (DateTime?)m.CreatedAt),
            })
            .ToListAsync(ct);

        var canWrite = member.Role is not Domain.Businesses.BusinessRole.Accountant;
        var items = new List<TomorrowNotice>();
        foreach (var r in rows)
        {
            var url = canWrite ? (await notifier.WhatsAppAsync(r.Id, WhatsAppNoticeKind.Reminder, null, ct))?.Url : null;
            items.Add(new TomorrowNotice(r.Id, r.StartAt, r.EndAt, r.Status, r.Contact ?? "Silinmiş müşteri", r.Service ?? "", r.Staff ?? "",
                url, r.RemindedAt));
        }

        var smsReminders = notifier.SmsEnabled
            && await db.Businesses.AsNoTracking().Where(b => b.Id == member.RequiredBusinessId).Select(b => b.ReminderHoursBefore > 0).FirstAsync(ct);
        return TypedResults.Ok(new TomorrowNotices(tomorrow, smsReminders, items));
    }

    private static readonly string[] SoftDeleteOnly = [QueryFilters.SoftDelete];

    private static async Task<IResult> FindAsync(Guid id, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var exists = await db.Appointments.AsNoTracking()
            .AnyAsync(a => a.Id == id && (!scope.Restricted || a.StaffMemberId == scope.StaffId), ct);
        return exists ? TypedResults.Ok() : Problems.NotFound("Randevu bulunamadı.");
    }

    private static async Task<IResult> Message(
        Guid id, WhatsAppMessageRequest request, CurrentMember member, AppDbContext db, AppointmentNotifier notifier, CancellationToken ct)
    {
        if (await FindAsync(id, member, db, ct) is not Ok)
        {
            return Problems.NotFound("Randevu bulunamadı.");
        }

        if (request.Kind == WhatsAppNoticeKind.Survey)
        {
            return Problems.BadRequest("invalid_kind", "Anket linki için survey-link ucunu kullanın.");
        }

        return await notifier.WhatsAppAsync(id, request.Kind, null, ct) is { } message
            ? TypedResults.Ok(new WhatsAppMessageResponse(message.Text, message.Url))
            : Problems.Conflict("no_mobile", "Müşterinin cep telefonu kayıtlı değil.");
    }

    private static async Task<IResult> Record(
        Guid id, WhatsAppMessageRequest request, CurrentMember member, AppDbContext db, AppointmentNotifier notifier, CancellationToken ct)
    {
        if (await FindAsync(id, member, db, ct) is not Ok)
        {
            return Problems.NotFound("Randevu bulunamadı.");
        }

        return await notifier.RecordWhatsAppAsync(id, request.Kind, ct) is null
            ? Problems.Conflict("no_mobile", "Müşterinin telefonu kayıtlı değil.")
            : TypedResults.NoContent();
    }

    private static async Task<IResult> SurveyLink(
        Guid id, CurrentMember member, AppDbContext db, AppointmentNotifier notifier, LinkTokenProtector protector,
        IOptions<PublicLinkOptions> links, TimeProvider clock, CancellationToken ct)
    {
        var scope = await StaffAccess.ScopeAsync(db, member, ct);
        var appointment = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && (!scope.Restricted || a.StaffMemberId == scope.StaffId), ct);
        if (appointment is null)
        {
            return Problems.NotFound("Randevu bulunamadı.");
        }

        if (appointment.Status != AppointmentStatus.Completed)
        {
            return Problems.Conflict("appointment_not_completed", "Anket linki tamamlanan randevu için gönderilir.");
        }

        var phone = await db.Contacts.AsNoTracking().Where(c => c.Id == appointment.ContactId).Select(c => c.Phone).FirstOrDefaultAsync(ct);
        if (phone is null || !PhoneVerification.IsMobile(phone))
        {
            return Problems.Conflict("no_mobile", "Müşterinin cep telefonu kayıtlı değil.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        string? code = null;
        var existing = await db.Surveys.AsNoTracking().Where(s => s.AppointmentId == id).OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is { Score: null } && existing.ExpiresAt > now)
        {
            code = protector.TryUnprotect(existing.ProtectedCode, existing.CodeHash);
        }
        else if (existing is { Score: not null })
        {
            return Problems.Conflict("survey_answered", "Müşteri bu randevu için anketi zaten cevapladı.");
        }

        if (code is null)
        {
            code = SurveyRules.NewCode();
            db.Surveys.Add(new Survey
            {
                AppointmentId = appointment.Id,
                ContactId = appointment.ContactId,
                StaffMemberId = appointment.StaffMemberId,
                CodeHash = SecureTokens.Hash(code),
                ProtectedCode = protector.Protect(code),
                ExpiresAt = now + SurveyRules.Lifetime,
            });
            await db.SaveChangesAsync(ct);
        }

        var surveyUrl = AutomationRunner.SurveyLink(links.Value.ShortBaseUrl, code);
        return await notifier.WhatsAppAsync(id, WhatsAppNoticeKind.Survey, surveyUrl, ct) is { } message
            ? TypedResults.Ok(new SurveyLinkResponse(message.Text, message.Url, surveyUrl))
            : Problems.Conflict("no_mobile", "Müşterinin cep telefonu kayıtlı değil.");
    }
}
