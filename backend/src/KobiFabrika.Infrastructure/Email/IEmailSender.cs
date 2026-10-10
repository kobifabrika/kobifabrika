namespace KobiFabrika.Infrastructure.Email;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>E-posta gönderimi. Geliştirmede SMTP ile Mailpit'e, canlıda sağlayıcıya (Sprint 8) gider.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
