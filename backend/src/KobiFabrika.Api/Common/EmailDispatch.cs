using KobiFabrika.Infrastructure.Email;

namespace KobiFabrika.Api.Common;

/// <summary>
/// E-postayı istek içinde gönderir; hata olursa loglar ama isteği düşürmez (kullanıcı "tekrar gönder" der).
/// Hangfire Sprint 5'te gelince kuyruğa alınacak (karar günlüğü).
/// </summary>
public sealed class EmailDispatch(IEmailSender sender, ILogger<EmailDispatch> logger)
{
    public async Task<bool> TrySendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "E-posta gönderilemedi: {Subject}", message.Subject);
            return false;
        }
    }
}
