using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Infrastructure.Messaging;

/// <summary>"Sms" bölümü.</summary>
public sealed class SmsOptions
{
    public const string Section = "Sms";

    /// <summary>
    /// Sahte sağlayıcı her SMS'i <c>sms+905…@sms.test</c> adresine e-posta olarak da yollar (Mailpit). Sadece E2E (ve isteğe
    /// bağlı Development) içindir; Production'da açıksa uygulama başlamaz.
    /// </summary>
    public bool MirrorToEmail { get; set; }

    /// <summary>İşletme başına günlük doğrulama SMS üst sınırı (platform ayarı; kod platformun kredisinden).</summary>
    public int VerificationDailyLimit { get; set; } = 100;

    public static bool IsSafe(SmsOptions options, IHostEnvironment env) => !(options.MirrorToEmail && env.IsProduction());

    public const string UnsafeMessage = "Sms:MirrorToEmail canlı ortamda (Production) açılamaz: müşteri SMS'leri e-postaya kopyalanır.";

    /// <summary>Yansıma e-postasının adresi: sms+905321234567@sms.test.</summary>
    public static string MirrorAddress(string e164) => $"sms{e164}@sms.test";
}

/// <summary>Gönderilecek SMS. RefType/RefId: ilgili kayıt (ör. "Appointment").</summary>
public sealed record SmsMessage(
    string To, string Body, MessagePurpose Purpose, Guid? ContactId = null, string? RefType = null, Guid? RefId = null);

/// <summary>
/// SMS gönderimi: önce MessageLog satırı, sonra sağlayıcı. Hata fırlatmaz; sonuç satırın Status/Error alanında.
/// Kiracı bağlamında çağrılır (MessageLog işletmeye aittir). Çağıranın bekleyen değişikliklerini de yazar; commit'ten sonra çağırın.
/// </summary>
public interface ISmsSender
{
    Task<MessageLog> SendAsync(SmsMessage message, CancellationToken ct);
}

/// <summary>SMS sağlayıcısı (gerçek sağlayıcı pilot öncesi). Dönen değer sağlayıcının mesaj Id'si.</summary>
public interface ISmsGateway
{
    string Name { get; }

    Task<string?> SendAsync(string to, string body, CancellationToken ct);
}

internal sealed class SmsSender(AppDbContext db, ISmsGateway gateway, TimeProvider clock, ILogger<SmsSender> logger) : ISmsSender
{
    /// <summary>Şimdilik segment başına 1 kredi (Sprint 6'da paket fiyatına göre).</summary>
    public const decimal CreditPerSegment = 1m;

    public async Task<MessageLog> SendAsync(SmsMessage message, CancellationToken ct)
    {
        var size = SmsSegments.Measure(message.Body);
        var body = message.Body.Length > MessageLog.MaxBodyLength ? message.Body[..MessageLog.MaxBodyLength] : message.Body;
        var log = new MessageLog
        {
            Channel = MessageChannel.Sms,
            To = message.To,
            Body = body,
            Purpose = message.Purpose,
            Provider = gateway.Name,
            Status = MessageStatus.Queued,
            Segments = size.Segments,
            CreditCost = size.Segments * CreditPerSegment,
            Billable = message.Purpose != MessagePurpose.Verification,
            ContactId = message.ContactId,
            RefType = message.RefType,
            RefId = message.RefId,
        };
        db.MessageLogs.Add(log);
        await db.SaveChangesAsync(ct);

        try
        {
            log.ProviderMessageId = await gateway.SendAsync(message.To, body, ct);
            log.Status = MessageStatus.Sent;
            log.SentAt = clock.GetUtcNow().UtcDateTime;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMS gönderilemedi: {Purpose} {To}", message.Purpose, message.To);
            log.Status = MessageStatus.Failed;
            log.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return log;
    }
}

/// <summary>
/// Sahte sağlayıcı (gerçek sağlayıcı pilot öncesi): SMS'i loga yazar; <see cref="SmsOptions.MirrorToEmail"/> açıksa
/// e-posta olarak Mailpit'e de yollar (E2E doğrulama kodunu oradan okur; test için ayrı HTTP ucu yok).
/// </summary>
internal sealed class FakeSmsGateway(IEmailSender email, IOptions<SmsOptions> options, ILogger<FakeSmsGateway> logger) : ISmsGateway
{
    public string Name => "fake";

    public async Task<string?> SendAsync(string to, string body, CancellationToken ct)
    {
        logger.LogInformation("[SMS] {To}: {Body}", to, body);
        if (options.Value.MirrorToEmail)
        {
            var subject = body.Length > 60 ? body[..60] : body;
            await email.SendAsync(new EmailMessage(SmsOptions.MirrorAddress(to), $"SMS: {subject}",
                $"<pre>{System.Net.WebUtility.HtmlEncode(body)}</pre>", body), ct);
        }

        return $"fake-{Guid.CreateVersion7():N}";
    }
}
