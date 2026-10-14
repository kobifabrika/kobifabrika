using KobiFabrika.Domain.Marketing;
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
/// SMS gönderimi: önce MessageLog satırı (ücretli mesajda kredi düşümüyle aynı transaction'da), sonra sağlayıcı.
/// Kredi yetmezse Status = NoCredit (gönderilmez, bir kez "kredi bitti" bildirimi); sağlayıcı hatasında Failed + kredi iadesi.
/// Hata fırlatmaz; sonuç satırın Status/Error alanında. Kiracı bağlamında, açık transaction olmadan çağrılır (commit'ten sonra);
/// çağıranın bekleyen değişikliklerini de yazar.
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

internal sealed class SmsSender(
    AppDbContext db, ISmsGateway gateway, SmsCredits credits, TimeProvider clock, ILogger<SmsSender> logger) : ISmsSender
{
    public async Task<MessageLog> SendAsync(SmsMessage message, CancellationToken ct)
    {
        var size = SmsSegments.Measure(message.Body);
        var body = message.Body.Length > MessageLog.MaxBodyLength ? message.Body[..MessageLog.MaxBodyLength] : message.Body;
        var billable = MessagePurposes.IsBillable(message.Purpose);
        var cost = SmsCredit.Cost(size.Segments);
        var log = new MessageLog
        {
            Channel = MessageChannel.Sms,
            To = message.To,
            Body = body,
            Purpose = message.Purpose,
            Provider = gateway.Name,
            Status = MessageStatus.Queued,
            Segments = size.Segments,
            CreditCost = cost,
            Billable = billable,
            ContactId = message.ContactId,
            RefType = message.RefType,
            RefId = message.RefId,
        };

        if (!billable)
        {
            db.MessageLogs.Add(log);
            await db.SaveChangesAsync(ct);
            await DeliverAsync(log, ct);
            await db.SaveChangesAsync(CancellationToken.None);
            return log;
        }

        // Önce kredi: log ve düşüm aynı transaction'da. Yetmezse gönderilmez (bakiye eksiye düşmez).
        int? balance;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.MessageLogs.Add(log);
            balance = await credits.TryDebitAsync(cost, ct);
            if (balance is null)
            {
                log.Status = MessageStatus.NoCredit;
                log.Error = "Kredi yetersiz";
            }
            else
            {
                db.SmsCreditTransactions.Add(new SmsCreditTransaction
                {
                    Amount = -cost, Kind = SmsCreditKind.Send, BalanceAfter = balance.Value, MessageLogId = log.Id,
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        if (balance is null)
        {
            await credits.OutOfCreditAsync(CancellationToken.None);
            return log;
        }

        await DeliverAsync(log, ct);
        if (log.Status == MessageStatus.Failed)
        {
            await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var after = await credits.CreditAsync(cost, CancellationToken.None);
            db.SmsCreditTransactions.Add(new SmsCreditTransaction
            {
                Amount = cost, Kind = SmsCreditKind.Refund, BalanceAfter = after, MessageLogId = log.Id, Note = "Gönderilemedi",
            });
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
            return log;
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await credits.AfterDebitAsync(balance.Value + cost, balance.Value, CancellationToken.None);
        return log;
    }

    private async Task DeliverAsync(MessageLog log, CancellationToken ct)
    {
        try
        {
            log.ProviderMessageId = await gateway.SendAsync(log.To, log.Body, ct);
            log.Status = MessageStatus.Sent;
            log.SentAt = clock.GetUtcNow().UtcDateTime;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMS gönderilemedi: {Purpose} {To}", log.Purpose, log.To);
            log.Status = MessageStatus.Failed;
            log.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
        }
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
