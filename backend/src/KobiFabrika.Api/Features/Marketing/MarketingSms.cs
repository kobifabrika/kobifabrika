using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Messaging;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>Otomasyon SMS'inin sonucu: gönderildiyse Log (Status Sent/Failed/NoCredit), gönderilmediyse Skip.</summary>
public sealed record MarketingSendResult(MessageLog? Log, RecipientSkipReason? Skip)
{
    public bool Sent => Log?.Status == MessageStatus.Sent;
}

/// <summary>
/// Tek kişiye pazarlama SMS'i (otomasyonlar; kampanya kendi akışında aynı kuralları uygular): müşteri, arşivde/anonim değil,
/// SMS izni var ve çıkış yapmamış, cep telefonu var, 09:00-21:00 içinde, bugün başka pazarlama SMS'i almamış (günlük pay);
/// metnin sonuna çıkış eki. Kredi yetmezse NoCredit (pay geri verilir). Kiracı bağlamında çağrılır.
/// </summary>
public sealed class MarketingSms(
    AppDbContext db, ISmsSender sms, CampaignSender campaigns, TimeProvider clock, IOptions<MarketingOptions> options, IOptions<PublicLinkOptions> links)
{
    public bool WindowOpen => options.Value.Window.IsOpen(clock.GetUtcNow().UtcDateTime);

    public async Task<MarketingSendResult> SendAsync(Contact contact, string body, MessagePurpose purpose, string refType, Guid refId, CancellationToken ct)
    {
        if (contact.IsArchived || contact.IsAnonymized || !contact.IsCustomer)
        {
            return new MarketingSendResult(null, RecipientSkipReason.Unavailable);
        }

        var revoked = await db.ContactConsents.AnyAsync(x => x.ContactId == contact.Id && x.Channel == ConsentChannel.Sms && !x.Granted, ct);
        if (MarketingEligibility.Check(contact.Phone, contact.SmsConsent, revoked) is { } skip)
        {
            return new MarketingSendResult(null, skip);
        }

        var today = BusinessTime.LocalDay(clock.GetUtcNow().UtcDateTime);
        if (!await campaigns.TryTakeDailySlotAsync(contact.Id, today, ct, purpose))
        {
            return new MarketingSendResult(null, RecipientSkipReason.DailyLimit);
        }

        var code = await campaigns.EnsureOptOutCodeAsync(contact.Id, ct);
        var text = MessageTemplate.WithFooter(body, OptOut.Footer(links.Value.ShortBaseUrl, code, options.Value.OptOutKeyword));
        var log = await sms.SendAsync(new SmsMessage(contact.Phone!, text, purpose, contact.Id, refType, refId), ct);
        if (log.Status != MessageStatus.Sent)
        {
            await campaigns.ReleaseDailySlotAsync(contact.Id, today, ct);
        }

        return new MarketingSendResult(log, log.Status == MessageStatus.NoCredit ? RecipientSkipReason.NoCredit : null);
    }
}
