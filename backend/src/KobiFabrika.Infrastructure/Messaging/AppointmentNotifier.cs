using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Infrastructure.Messaging;

public enum AppointmentSmsKind
{
    /// <summary>Online randevu alındı (Beklemede) veya onaylandı (otomatik onay).</summary>
    Booked,

    /// <summary>Beklemedeki randevu işletmece onaylandı.</summary>
    Confirmed,

    Reminder,
    Moved,
    Cancelled,
    Rejected,
}

/// <summary>
/// Müşteriye randevu SMS'leri (bilgilendirme; pazarlama izni aranmaz). Müşterinin telefonu TR cep değilse SMS gitmez (null).
/// İptal linki /r/[slug]/randevu/[token]: token bir kez üretilir, özeti ve şifreli hali randevuda saklanır.
/// Kiracı bağlamında çağrılır; commit'ten sonra çağrılmalı.
/// </summary>
public sealed class AppointmentNotifier(
    AppDbContext db, ISmsSender sms, LinkTokenProtector protector, IOptions<AppOptions> app, TimeProvider clock)
{
    /// <summary>Randevunun müşteri linki token'ı (yoksa üretir; eşzamanlı ikinci üretim ilkini kullanır).</summary>
    public async Task<string> EnsureManageTokenAsync(Guid appointmentId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId)
                .Select(a => new { a.ManageToken, a.ManageTokenHash }).FirstAsync(ct);
            if (row.ManageTokenHash is { } hash && protector.TryUnprotect(row.ManageToken, hash) is { } existing)
            {
                return existing;
            }

            var (token, newHash) = SecureTokens.Create();
            var protectedToken = protector.Protect(token);
            var oldHash = row.ManageTokenHash;
            var updated = await db.Appointments
                .Where(a => a.Id == appointmentId && (oldHash == null ? a.ManageTokenHash == null : a.ManageTokenHash == oldHash))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ManageTokenHash, newHash).SetProperty(a => a.ManageToken, protectedToken), ct);
            if (updated == 1)
            {
                return token;
            }
        }

        throw new InvalidOperationException("Randevu linki oluşturulamadı.");
    }

    public string ManageLink(string slug, string token) => app.Value.Link($"/r/{slug}/randevu/{token}");

    public async Task<MessageLog?> SendAsync(Guid appointmentId, AppointmentSmsKind kind, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().Where(x => x.Id == appointmentId)
            .Select(x => new
            {
                x.Id, x.ContactId, x.StartAt, x.BusinessId,
                Phone = db.Contacts.Where(c => c.Id == x.ContactId).Select(c => c.Phone).FirstOrDefault(),
                Service = db.Products.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(p => p.Id == x.ProductId).Select(p => p.Name).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (a?.Phone is not { } phone || !PhoneVerification.IsMobile(phone))
        {
            return null;
        }

        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == a.BusinessId)
            .Select(b => new { b.Name, b.Slug, b.Phone }).FirstAsync(ct);
        var service = a.Service ?? "";
        var businessPhone = business.Phone is { } p ? FormatPhone(p) : null;

        async Task<string> Link() => ManageLink(business.Slug, await EnsureManageTokenAsync(a.Id, ct));

        var (purpose, body) = kind switch
        {
            AppointmentSmsKind.Booked => (MessagePurpose.AppointmentConfirmation, SmsTexts.Booked(business.Name, a.StartAt, service,
                await db.Appointments.Where(x => x.Id == a.Id).Select(x => x.Status).FirstAsync(ct) == AppointmentStatus.Confirmed, await Link())),
            AppointmentSmsKind.Confirmed => (MessagePurpose.AppointmentConfirmation, SmsTexts.Confirmed(business.Name, a.StartAt, service, await Link())),
            AppointmentSmsKind.Reminder => (MessagePurpose.AppointmentReminder,
                SmsTexts.Reminder(business.Name, a.StartAt, clock.GetUtcNow().UtcDateTime, service, await Link())),
            AppointmentSmsKind.Moved => (MessagePurpose.AppointmentMoved, SmsTexts.Moved(business.Name, a.StartAt, service, await Link())),
            AppointmentSmsKind.Cancelled => (MessagePurpose.AppointmentCancelled, SmsTexts.Cancelled(business.Name, a.StartAt, service, businessPhone)),
            _ => (MessagePurpose.AppointmentRejected, SmsTexts.Rejected(business.Name, a.StartAt, service, businessPhone)),
        };

        return await sms.SendAsync(new SmsMessage(phone, body, purpose, a.ContactId, "Appointment", a.Id), ct);
    }

    /// <summary>+902125554433 → 0212 555 44 33.</summary>
    public static string FormatPhone(string e164)
    {
        if (e164.Length != 13 || !e164.StartsWith("+90", StringComparison.Ordinal))
        {
            return e164;
        }

        var n = "0" + e164[3..];
        return $"{n[..4]} {n[4..7]} {n[7..9]} {n[9..]}";
    }
}
