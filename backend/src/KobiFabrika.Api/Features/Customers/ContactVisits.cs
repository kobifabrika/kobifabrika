using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>
/// Müşterinin ziyaretleri (segmentler, geri kazanma, kaybedilme riski): tamamlanmış satışlar ve tamamlanmış randevular.
/// Son ziyaret anı (LastVisitAt) en yenisi; ziyaret sayısı ve ortalama aralık farklı İstanbul günleri üzerinden (aynı gün satış +
/// randevu tek ziyaret; randevudan çevrilen satış çift sayılmaz). Migration'daki BackfillVisitStatsSql aynı kural.
/// </summary>
public static class ContactVisits
{
    /// <summary>Yeni ziyaret (satış, tamamlanan randevu): istatistik baştan hesaplanır.</summary>
    public static Task TouchAsync(AppDbContext db, Guid contactId, DateTime at, CancellationToken ct) => RecomputeAsync(db, contactId, ct);

    /// <summary>İptal, geri alma veya yeni ziyaretten sonra baştan hesaplar.</summary>
    public static async Task RecomputeAsync(AppDbContext db, Guid contactId, CancellationToken ct)
    {
        var sales = await db.Sales.Where(s => s.ContactId == contactId && s.Status == SaleStatus.Completed).Select(s => s.Date).ToListAsync(ct);
        var appointments = await db.Appointments.Where(a => a.ContactId == contactId && a.Status == AppointmentStatus.Completed)
            .Select(a => a.StartAt).ToListAsync(ct);
        var all = sales.Concat(appointments).ToList();
        DateTime? last = all.Count > 0 ? all.Max() : null;
        var stats = VisitStats.From(all.Select(BusinessTime.LocalDay));
        await db.Contacts.Where(c => c.Id == contactId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.LastVisitAt, last)
            .SetProperty(c => c.VisitCount, stats.Count)
            .SetProperty(c => c.FirstVisitOn, stats.First)
            .SetProperty(c => c.AvgVisitIntervalDays, stats.AvgIntervalDays), ct);
    }
}
