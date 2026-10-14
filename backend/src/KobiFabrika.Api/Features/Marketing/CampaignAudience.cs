using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>Kampanya hedefindeki bir müşteri ve pazarlama uygunluğu.</summary>
public sealed record AudienceMember(
    Guid Id, string? FirstName, string DisplayName, string? Phone, bool SmsConsent, bool HasRevokedSms, bool SentToday)
{
    public RecipientSkipReason? Skip => MarketingEligibility.Check(Phone, SmsConsent, HasRevokedSms) switch
    {
        null when SentToday => RecipientSkipReason.DailyLimit,
        var reason => reason,
    };
}

/// <summary>
/// Kampanya hedefini müşterilere çevirir. Sadece müşteriler (tedarikçi değil), arşivde olmayan, silinmemiş ve anonimleştirilmemiş
/// kayıtlar. Filtre ve segmentler müşteri listesindekiyle aynı (<see cref="ContactQueries"/>); kiracı filtresi sayesinde başka
/// işletmenin segmenti/etiketi hiçbir müşteriyle eşleşmez.
/// </summary>
public static class CampaignAudience
{
    public sealed record Resolved(IQueryable<Contact> Query, string TargetName, string? FilterJson);

    public static IQueryable<Contact> Base(AppDbContext db) =>
        db.Contacts.Where(c => c.IsCustomer && !c.IsArchived && !c.IsAnonymized);

    /// <summary>Hedefi çözer; segment bulunamazsa null.</summary>
    public static async Task<Resolved?> ResolveAsync(AppDbContext db, CampaignTarget target, TimeProvider clock, CancellationToken ct)
    {
        var query = Base(db);
        var now = clock.GetUtcNow().UtcDateTime;
        if (target.SegmentCode is { } code)
        {
            var name = SystemSegments.All.First(s => s.Code == code).Name;
            return new Resolved(query.ApplySystemSegment(code, clock), name, null);
        }

        if (target.SegmentId is { } segmentId)
        {
            var segment = await db.Segments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == segmentId, ct);
            if (segment is null)
            {
                return null;
            }

            var definition = ContactFilter.Deserialize(segment.Definition);
            return new Resolved(query.Apply(definition, db, now), segment.Name, segment.Definition);
        }

        var filter = target.Filter ?? new ContactFilter();
        return new Resolved(query.Apply(filter, db, now), "Filtre", filter.Serialize());
    }

    /// <summary>Kampanya kaydından hedef (zamanlanmış kampanyada alıcılar gönderim anında çözülür).</summary>
    public static CampaignTarget TargetOf(Campaign campaign) => campaign.SegmentCode is { } code
        ? new CampaignTarget(SegmentCode: code)
        : new CampaignTarget(Filter: campaign.Filter is { } json ? ContactFilter.Deserialize(json) : new ContactFilter());

    public static Task<List<AudienceMember>> LoadAsync(AppDbContext db, IQueryable<Contact> query, DateOnly today, CancellationToken ct) =>
        query.AsNoTracking()
            .OrderBy(c => c.DisplayName).ThenBy(c => c.Id)
            .Select(c => new AudienceMember(
                c.Id, c.FirstName, c.DisplayName, c.Phone, c.SmsConsent,
                db.ContactConsents.Any(x => x.ContactId == c.Id && x.Channel == ConsentChannel.Sms && !x.Granted),
                db.MarketingDailySends.Any(d => d.ContactId == c.Id && d.LocalDate == today)))
            .ToListAsync(ct);

    /// <summary>Segmentin bir uyarısı: bakiyeye dayanıyor mu (Personel göremez; kampanya zaten Sahip/Yönetici).</summary>
    public static bool UsesBalance(CampaignTarget target) =>
        target.Filter?.UsesBalance == true || (target.SegmentCode is { } code && ContactQueries.SystemSegmentUsesBalance(code));

    public static DateOnly LocalDay(DateTime utc) => BusinessTime.LocalDay(utc);
}
