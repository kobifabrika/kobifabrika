using KobiFabrika.Infrastructure.Email;

namespace KobiFabrika.Api.Common;

/// <summary>
/// E-postayı kuyruğa alır (<see cref="IEmailQueue"/>; Hangfire gönderir, hata olursa yeniden dener). Kuyruğa yazılamazsa loglar
/// ama isteği düşürmez (kullanıcı "tekrar gönder" der).
/// </summary>
public sealed class EmailDispatch(IEmailQueue queue, ILogger<EmailDispatch> logger)
{
    public async Task<bool> TrySendAsync(EmailMessage message, CancellationToken cancellationToken, string purpose = "system")
    {
        try
        {
            await queue.EnqueueAsync(message, purpose, ct: cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "E-posta kuyruğa alınamadı: {Subject}", message.Subject);
            return false;
        }
    }
}
