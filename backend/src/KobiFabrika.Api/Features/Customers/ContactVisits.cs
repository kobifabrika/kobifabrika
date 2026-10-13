using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>
/// Müşterinin son ziyareti (segmentler, "60 gündür gelmeyenler"): tamamlanmış satışlar ve tamamlanmış randevuların en yenisi.
/// </summary>
public static class ContactVisits
{
    /// <summary>Daha yeni bir ziyaret geldiyse ileri alır.</summary>
    public static Task TouchAsync(AppDbContext db, Guid contactId, DateTime at, CancellationToken ct) =>
        db.Contacts.Where(c => c.Id == contactId && (c.LastVisitAt == null || c.LastVisitAt < at))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastVisitAt, at), ct);

    /// <summary>İptal veya geri almadan sonra baştan hesaplar.</summary>
    public static async Task RecomputeAsync(AppDbContext db, Guid contactId, CancellationToken ct)
    {
        var lastSale = await db.Sales.Where(s => s.ContactId == contactId && s.Status == SaleStatus.Completed)
            .MaxAsync(s => (DateTime?)s.Date, ct);
        var lastAppointment = await db.Appointments.Where(a => a.ContactId == contactId && a.Status == AppointmentStatus.Completed)
            .MaxAsync(a => (DateTime?)a.StartAt, ct);
        var last = lastSale > lastAppointment || lastAppointment is null ? lastSale : lastAppointment;
        await db.Contacts.Where(c => c.Id == contactId).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastVisitAt, last), ct);
    }
}
