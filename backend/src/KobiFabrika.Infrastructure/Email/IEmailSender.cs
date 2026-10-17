namespace KobiFabrika.Infrastructure.Email;

/// <summary>
/// Gönderilecek e-posta. SenderBusinessName doluysa işletme adına gider: gönderen adı "{İşletme} · KobiFabrika", Reply-To işletmenin
/// e-postası (kuyruk doldurur). ListUnsubscribe: pazarlama e-postasında çıkış linki (List-Unsubscribe başlığı).
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string? ReplyTo = null,
    string? FromName = null,
    string? ListUnsubscribe = null,
    string? SenderBusinessName = null);

/// <summary>E-posta gönderimi. Geliştirmede SMTP ile Mailpit'e, canlıda Brevo SMTP relay'e gider.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
