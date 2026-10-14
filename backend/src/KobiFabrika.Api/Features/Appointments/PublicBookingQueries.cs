using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Appointments;

/// <summary>Online randevuda seçilebilen personel. ServiceIds null: bütün hizmetleri verir.</summary>
public sealed record BookableStaff(Guid Id, string FullName, string? Title, int SortOrder, Guid? UserId, IReadOnlySet<Guid>? ServiceIds)
{
    public bool Gives(Guid serviceId) => ServiceIds is null || ServiceIds.Contains(serviceId);
}

/// <summary>
/// Online randevu sayfasının verisi (kiracı bağlamında): personel aktif ve "online randevuda görünsün"; hizmet aktif, süresi tanımlı
/// ve en az bir uygun personeli var. Boş saatler <see cref="Availability"/> ile; en erken bildirim, en fazla gün ve saat aralığı
/// işletme ayarından.
/// </summary>
public static class PublicBookingQueries
{
    public static async Task<(List<Product> Services, List<BookableStaff> Staff)> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var staff = await db.StaffMembers.AsNoTracking().Where(s => s.IsActive && s.ShowInBooking)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.FullName).ToListAsync(ct);
        var staffIds = staff.Select(s => s.Id).ToList();
        var links = (await db.StaffServices.AsNoTracking().Where(x => staffIds.Contains(x.StaffMemberId)).ToListAsync(ct))
            .ToLookup(x => x.StaffMemberId, x => x.ProductId);
        var rows = staff.Select(s => new BookableStaff(s.Id, s.FullName, s.Title, s.SortOrder, s.UserId,
            links[s.Id].Any() ? links[s.Id].ToHashSet() : null)).ToList();

        var services = (await db.Products.AsNoTracking()
                .Where(p => p.Type == ProductType.Service && p.IsActive && p.DurationMinutes > 0)
                .OrderBy(p => p.Name).ToListAsync(ct))
            .Where(p => rows.Any(s => s.Gives(p.Id)))
            .ToList();
        return (services, rows);
    }

    public static async Task<(int Staff, int Services)> CountsAsync(AppDbContext db, CancellationToken ct)
    {
        var (services, staff) = await LoadAsync(db, ct);
        return (staff.Count, services.Count);
    }

    /// <summary>
    /// Gün → (personel, boş başlangıç UTC) listesi; [from, to] aralığı online pencereyle (bugün … bugün + en fazla gün) kırpılır.
    /// <paramref name="includeAppointments"/> false ise sadece çalışma saati, tatil ve kapalı zamanlar (dolu saati ayırt etmek için).
    /// </summary>
    public static async Task<Dictionary<DateOnly, List<(Guid StaffId, DateTime Start)>>> SlotsAsync(
        AppDbContext db, Business business, Product service, IReadOnlyList<BookableStaff> staff, DateOnly from, DateOnly to, DateTime now,
        CancellationToken ct, bool includeAppointments = true)
    {
        var today = BusinessTime.LocalDay(now);
        var last = BookingWindow.LastDay(today, business.BookingMaxDaysAhead);
        from = from < today ? today : from;
        to = to > last ? last : to;
        var result = new Dictionary<DateOnly, List<(Guid, DateTime)>>();
        if (from > to || staff.Count == 0)
        {
            return result;
        }

        var plans = await AvailabilityQueries.LoadAsync(db, staff.Select(s => s.Id).ToList(), from, to, includeAppointments, null, ct);
        var earliest = BookingWindow.EarliestStart(now, business.BookingMinNoticeMinutes);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var list = new List<(Guid, DateTime)>();
            foreach (var s in staff)
            {
                list.AddRange(Availability.Slots(plans[(s.Id, day)], service.DurationMinutes!.Value, business.BookingSlotMinutes, earliest)
                    .Select(start => (s.Id, start)));
            }

            result[day] = list;
        }

        return result;
    }

    /// <summary>Personellerin o günkü dolu (randevu) dakikaları: "Fark etmez"de en az dolu personel seçilir.</summary>
    public static async Task<Dictionary<Guid, int>> BookedMinutesAsync(AppDbContext db, IReadOnlyCollection<Guid> staffIds, DateOnly day, CancellationToken ct)
    {
        var (start, end) = BusinessTime.DayRangeUtc(day);
        return await db.Appointments.AsNoTracking()
            .Where(a => staffIds.Contains(a.StaffMemberId) && a.Status != AppointmentStatus.Cancelled && a.StartAt < end && a.EndAt > start)
            .GroupBy(a => a.StaffMemberId)
            .Select(g => new { g.Key, Minutes = g.Sum(a => EF.Functions.DateDiffMinute(a.StartAt, a.EndAt)) })
            .ToDictionaryAsync(x => x.Key, x => x.Minutes, ct);
    }

    /// <summary>Online randevu bildirimini alanlar: Sahip, Yönetici ve personelin bağlı (aktif üye) kullanıcısı.</summary>
    public static async Task<List<Guid>> RecipientsAsync(AppDbContext db, Guid businessId, Guid staffId, CancellationToken ct)
    {
        var staffUser = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(s => s.Id == staffId)
            .Select(s => s.UserId).FirstOrDefaultAsync(ct);
        return await db.BusinessMemberships
            .Where(m => m.BusinessId == businessId && m.IsActive && (RoleSets.Managers.Contains(m.Role) || m.UserId == staffUser))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    public static string CalendarLink(Guid appointmentId, DateTime startUtc) =>
        $"/panel/randevular?tarih={BusinessTime.LocalDay(startUtc):yyyy-MM-dd}&randevu={appointmentId}";
}
