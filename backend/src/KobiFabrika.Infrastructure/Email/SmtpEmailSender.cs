using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace KobiFabrika.Infrastructure.Email;

/// <summary>MailKit ile SMTP gönderimi (geliştirmede Mailpit, http://localhost:8025).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName ?? settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        if ((message.ReplyTo ?? settings.ReplyToAddress) is { } replyTo && MailboxAddress.TryParse(replyTo, out var reply))
        {
            mime.ReplyTo.Add(reply);
        }

        if (message.ListUnsubscribe is { } unsubscribe)
        {
            mime.Headers.Add("List-Unsubscribe", $"<{unsubscribe}>");
            mime.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            settings.Smtp.Host,
            settings.Smtp.Port,
            !settings.Smtp.UseSsl ? SecureSocketOptions.None
                : settings.Smtp.Port == 465 ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls,
            cancellationToken);

        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            await client.AuthenticateAsync(settings.Smtp.UserName, settings.Smtp.Password ?? "", cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
