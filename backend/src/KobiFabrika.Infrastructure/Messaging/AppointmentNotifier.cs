using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Email;
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
/// Müşteriye randevu bildirimleri (bilgilendirme; pazarlama izni aranmaz). Kanal (karar 2026-10-17): SMS açıksa
/// (<c>Messaging:SmsEnabled</c>) ve müşterinin cep telefonu varsa SMS; değilse e-posta (online randevuda girilen e-posta, yoksa
/// müşteri kartındaki); ikisi de yoksa gönderilmez (null). E-posta MessageLog'a Email kanalıyla yazılır (zaman çizelgesi, "zaten
/// bildirildi" kontrolü). WhatsApp mesajları panelden elle (<see cref="WhatsAppAsync"/>). İptal linki /r/[slug]/randevu/[token]:
/// token bir kez üretilir, özeti ve şifreli hali randevuda saklanır. Kiracı bağlamında, commit'ten sonra çağrılır.
/// </summary>
public sealed class AppointmentNotifier(
    AppDbContext db, ISmsSender sms, IEmailQueue emails, LinkTokenProtector protector, IOptions<AppOptions> app,
    IOptionsMonitor<MessagingOptions> messaging, TimeProvider clock)
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

    private sealed record Info(
        Guid Id, Guid ContactId, DateTime StartAt, Guid BusinessId, AppointmentStatus Status, string? Phone, string? ContactEmail,
        string? BookingEmail, string FirstName, string DisplayName, string Service, string? Staff);

    private sealed record BusinessInfo(string Name, string Slug, string? Phone, string? Address, string? City, string? District,
        string? LogoUrl, bool BookingOpen);

    private Task<Info?> LoadAsync(Guid appointmentId, CancellationToken ct) =>
        db.Appointments.AsNoTracking().Where(x => x.Id == appointmentId)
            .Select(x => new Info(
                x.Id, x.ContactId, x.StartAt, x.BusinessId, x.Status,
                db.Contacts.Where(c => c.Id == x.ContactId).Select(c => c.Phone).FirstOrDefault(),
                db.Contacts.Where(c => c.Id == x.ContactId).Select(c => c.Email).FirstOrDefault(),
                x.BookingEmail,
                db.Contacts.Where(c => c.Id == x.ContactId).Select(c => c.FirstName ?? c.DisplayName).FirstOrDefault() ?? "",
                db.Contacts.Where(c => c.Id == x.ContactId).Select(c => c.DisplayName).FirstOrDefault() ?? "",
                db.Products.IgnoreQueryFilters(SoftDeleteOnly).Where(p => p.Id == x.ProductId).Select(p => p.Name).FirstOrDefault() ?? "",
                db.StaffMembers.IgnoreQueryFilters(SoftDeleteOnly).Where(s => s.Id == x.StaffMemberId).Select(s => s.FullName).FirstOrDefault()))
            .FirstOrDefaultAsync(ct);

    private static readonly string[] SoftDeleteOnly = [QueryFilters.SoftDelete];

    private Task<BusinessInfo> BusinessAsync(Guid businessId, CancellationToken ct) =>
        db.Businesses.AsNoTracking().Where(b => b.Id == businessId)
            .Select(b => new BusinessInfo(b.Name, b.Slug, b.Phone, b.Address, b.City, b.District, b.LogoUrl,
                b.BookingEnabled && (b.Modules & Domain.Businesses.BusinessModules.Appointments) != 0))
            .FirstAsync(ct);

    private static string? AddressOf(BusinessInfo b) =>
        string.Join(", ", new[] { b.Address, b.District, b.City }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } a ? a : null;

    /// <summary>Müşterinin online randevu sayfası (yeni randevu için); sayfa kapalıysa null.</summary>
    private string? BookingLink(BusinessInfo b) => b.BookingOpen ? app.Value.Link($"/r/{b.Slug}") : null;

    public bool SmsEnabled => messaging.CurrentValue.SmsEnabled;

    public async Task<MessageLog?> SendAsync(Guid appointmentId, AppointmentSmsKind kind, CancellationToken ct)
    {
        var a = await LoadAsync(appointmentId, ct);
        if (a is null)
        {
            return null;
        }

        var business = await BusinessAsync(a.BusinessId, ct);
        async Task<string> Link() => ManageLink(business.Slug, await EnsureManageTokenAsync(a.Id, ct));

        if (SmsEnabled && a.Phone is { } phone && PhoneVerification.IsMobile(phone))
        {
            var service = a.Service;
            var businessPhone = business.Phone is { } p ? FormatPhone(p) : null;
            var (purpose, body) = kind switch
            {
                AppointmentSmsKind.Booked => (MessagePurpose.AppointmentConfirmation,
                    SmsTexts.Booked(business.Name, a.StartAt, service, a.Status == AppointmentStatus.Confirmed, await Link())),
                AppointmentSmsKind.Confirmed => (MessagePurpose.AppointmentConfirmation, SmsTexts.Confirmed(business.Name, a.StartAt, service, await Link())),
                AppointmentSmsKind.Reminder => (MessagePurpose.AppointmentReminder,
                    SmsTexts.Reminder(business.Name, a.StartAt, clock.GetUtcNow().UtcDateTime, service, await Link())),
                AppointmentSmsKind.Moved => (MessagePurpose.AppointmentMoved, SmsTexts.Moved(business.Name, a.StartAt, service, await Link())),
                AppointmentSmsKind.Cancelled => (MessagePurpose.AppointmentCancelled, SmsTexts.Cancelled(business.Name, a.StartAt, service, businessPhone)),
                _ => (MessagePurpose.AppointmentRejected, SmsTexts.Rejected(business.Name, a.StartAt, service, businessPhone)),
            };

            return await sms.SendAsync(new SmsMessage(phone, body, purpose, a.ContactId, "Appointment", a.Id), ct);
        }

        var to = ValidEmail(a.BookingEmail) ?? ValidEmail(a.ContactEmail);
        if (to is null)
        {
            return null;
        }

        var emailKind = kind switch
        {
            AppointmentSmsKind.Booked => a.Status == AppointmentStatus.Confirmed
                ? EmailTemplates.AppointmentEmailKind.Booked
                : EmailTemplates.AppointmentEmailKind.Requested,
            AppointmentSmsKind.Confirmed => EmailTemplates.AppointmentEmailKind.Confirmed,
            AppointmentSmsKind.Reminder => EmailTemplates.AppointmentEmailKind.Reminder,
            AppointmentSmsKind.Moved => EmailTemplates.AppointmentEmailKind.Moved,
            AppointmentSmsKind.Cancelled => EmailTemplates.AppointmentEmailKind.Cancelled,
            _ => EmailTemplates.AppointmentEmailKind.Rejected,
        };
        var manage = emailKind is EmailTemplates.AppointmentEmailKind.Cancelled or EmailTemplates.AppointmentEmailKind.Rejected ? null : await Link();
        var sender = new EmailSender(business.Name, app.Value.ApiLink(business.LogoUrl), business.Phone is { } bp ? FormatPhone(bp) : null,
            AddressOf(business));
        var now = clock.GetUtcNow().UtcDateTime;
        var message = EmailTemplates.Appointment(emailKind, to, sender, a.DisplayName, a.StartAt, a.Service, a.Staff, manage,
            BookingLink(business), now);
        await emails.EnqueueAsync(message, "appointment-" + emailKind.ToString().ToLowerInvariant(), ct: ct);

        var log = new MessageLog
        {
            Channel = MessageChannel.Email,
            To = to,
            Body = message.Subject.Length > 1000 ? message.Subject[..1000] : message.Subject,
            Purpose = PurposeOf(kind),
            Provider = "email",
            Status = MessageStatus.Sent,
            ContactId = a.ContactId,
            RefType = "Appointment",
            RefId = a.Id,
            SentAt = now,
        };
        db.MessageLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log;
    }

    private static MessagePurpose PurposeOf(AppointmentSmsKind kind) => kind switch
    {
        AppointmentSmsKind.Booked or AppointmentSmsKind.Confirmed => MessagePurpose.AppointmentConfirmation,
        AppointmentSmsKind.Reminder => MessagePurpose.AppointmentReminder,
        AppointmentSmsKind.Moved => MessagePurpose.AppointmentMoved,
        AppointmentSmsKind.Cancelled => MessagePurpose.AppointmentCancelled,
        _ => MessagePurpose.AppointmentRejected,
    };

    /// <summary>
    /// Panelden WhatsApp ile gönderilecek hazır mesaj ve wa.me linki (müşterinin cep telefonu yoksa null). Gönderimi kullanıcı yapar;
    /// dokunma kaydı ayrı (<see cref="RecordWhatsAppAsync"/>).
    /// </summary>
    public async Task<WhatsAppMessage?> WhatsAppAsync(Guid appointmentId, WhatsAppNoticeKind kind, string? surveyLink, CancellationToken ct)
    {
        var a = await LoadAsync(appointmentId, ct);
        if (a?.Phone is not { } phone || !PhoneVerification.IsMobile(phone))
        {
            return null;
        }

        var business = await BusinessAsync(a.BusinessId, ct);
        string text;
        if (kind == WhatsAppNoticeKind.Survey)
        {
            text = WhatsAppTexts.Survey(a.FirstName, business.Name, surveyLink ?? throw new ArgumentNullException(nameof(surveyLink)));
        }
        else
        {
            var manage = kind is WhatsAppNoticeKind.Cancelled or WhatsAppNoticeKind.Rejected
                ? null
                : ManageLink(business.Slug, await EnsureManageTokenAsync(a.Id, ct));
            text = WhatsAppTexts.Appointment(kind, a.FirstName, business.Name, a.StartAt, clock.GetUtcNow().UtcDateTime, a.Service, manage,
                business.Phone is { } p ? FormatPhone(p) : null, BookingLink(business));
        }

        return new WhatsAppMessage(text, WhatsAppTexts.Link(phone, text), phone);
    }

    /// <summary>WhatsApp düğmesine dokunuldu: MessageLog (WhatsApp, Handed) — "Hatırlatıldı" işareti ve zaman çizelgesi.</summary>
    public async Task<MessageLog?> RecordWhatsAppAsync(Guid appointmentId, WhatsAppNoticeKind kind, CancellationToken ct)
    {
        var a = await LoadAsync(appointmentId, ct);
        if (a?.Phone is not { } phone)
        {
            return null;
        }

        var log = new MessageLog
        {
            Channel = MessageChannel.WhatsApp,
            To = phone,
            Body = "WhatsApp: " + kind,
            Purpose = kind switch
            {
                WhatsAppNoticeKind.Confirmed => MessagePurpose.AppointmentConfirmation,
                WhatsAppNoticeKind.Reminder => MessagePurpose.AppointmentReminder,
                WhatsAppNoticeKind.Moved => MessagePurpose.AppointmentMoved,
                WhatsAppNoticeKind.Cancelled => MessagePurpose.AppointmentCancelled,
                WhatsAppNoticeKind.Rejected => MessagePurpose.AppointmentRejected,
                _ => MessagePurpose.Survey,
            },
            Provider = "whatsapp",
            Status = MessageStatus.Handed,
            ContactId = a.ContactId,
            RefType = "Appointment",
            RefId = a.Id,
            SentAt = clock.GetUtcNow().UtcDateTime,
        };
        db.MessageLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log;
    }

    private static string? ValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _) ? email.Trim() : null;

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

/// <summary>WhatsApp'ta açılacak hazır mesaj: metin, wa.me linki, müşterinin telefonu.</summary>
public sealed record WhatsAppMessage(string Text, string Url, string Phone);
