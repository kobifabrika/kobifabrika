using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Appointments;

/// <summary>
/// Randevunun saatinin tek yazıldığı yer (oluşturma, taşıma, süre/personel değiştirme). Transaction içinde personel satırı
/// <c>UPDLOCK, HOLDLOCK</c> ile kilitlenir; aynı personele gelen istekler sıraya girer, çakışma sorgusu kilitten sonra çalışır.
/// Böylece aynı saate eşzamanlı 10 istekten yalnız biri kaydolur; farklı personellerin istekleri birbirini beklemez.
/// Kilit sırası: personel → randevu → (satış zinciri: numara → satış → cari → kasa → stok).
/// </summary>
public sealed class AppointmentBooking(AppDbContext db, LedgerService ledger, ITenantContext tenant)
{
    /// <summary>
    /// Randevuyu (eklenmiş veya değiştirilmiş, izlenen entity) kaydeder. Personel pasif/silinmişse 409 <c>staff_inactive</c>,
    /// saat doluysa 409 <c>appointment_overlap</c>.
    /// </summary>
    public Task SaveAsync(Appointment appointment, CancellationToken ct) =>
        ledger.InTransactionAsync(async () =>
        {
            var businessId = tenant.BusinessId ?? throw new TenantViolationException("Kiracı bağlamı olmadan randevu yazılamaz.");
            var staffId = appointment.StaffMemberId;
            var locked = await db.Database.SqlQuery<Guid>($"""
                SELECT [Id] AS [Value] FROM [StaffMembers] WITH (UPDLOCK, HOLDLOCK)
                WHERE [Id] = {staffId} AND [BusinessId] = {businessId} AND [IsDeleted] = 0 AND [IsActive] = 1
                """).ToListAsync(ct);
            if (locked.Count == 0)
            {
                throw new ConflictException("staff_inactive", "Personel pasif veya silinmiş; randevu verilemez.");
            }

            if (await OverlapMessageAsync(db, staffId, appointment.StartAt, appointment.EndAt, appointment.Id, ct) is { } overlap)
            {
                throw new ConflictException("appointment_overlap", overlap);
            }

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    /// <summary>Personelin bu aralıkla çakışan (iptal olmayan) randevusu varsa Türkçe açıklama.</summary>
    public static async Task<string?> OverlapMessageAsync(
        AppDbContext db, Guid staffId, DateTime start, DateTime end, Guid? excludeId, CancellationToken ct)
    {
        var conflict = await db.Appointments.AsNoTracking()
            .Where(a => a.StaffMemberId == staffId && a.Id != excludeId && a.Status != AppointmentStatus.Cancelled
                && a.StartAt < end && a.EndAt > start)
            .OrderBy(a => a.StartAt)
            .Select(a => new { a.StartAt, a.EndAt, Staff = db.StaffMembers.Where(s => s.Id == a.StaffMemberId).Select(s => s.FullName).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        return conflict is null
            ? null
            : $"{conflict.Staff} {BusinessTime.ToLocal(conflict.StartAt):HH:mm}-{BusinessTime.ToLocal(conflict.EndAt):HH:mm} arasında başka bir randevuda.";
    }
}

/// <summary>
/// Boş saat ve çalışma saati hesabı için personellerin gün planlarını tek seferde yükler (işletme haftası, tatiller, personel saatleri,
/// kapalı zamanlar ve isteğe bağlı randevular).
/// </summary>
public static class AvailabilityQueries
{
    public static async Task<Dictionary<(Guid StaffId, DateOnly Day), StaffDayPlan>> LoadAsync(
        AppDbContext db, IReadOnlyCollection<Guid> staffIds, DateOnly from, DateOnly to, bool includeAppointments, Guid? excludeAppointmentId,
        CancellationToken ct)
    {
        var (startUtc, endUtc) = BusinessTime.RangeUtc(from, to);
        var week = (await db.WorkingHours.AsNoTracking().ToListAsync(ct)).ToDictionary(w => w.Day);
        var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to).ToListAsync(ct))
            .ToDictionary(h => h.Date, h => h.Name);
        var hours = (await db.StaffWorkingHours.AsNoTracking().Where(h => staffIds.Contains(h.StaffMemberId)).ToListAsync(ct))
            .ToLookup(h => (h.StaffMemberId, h.Day));
        var busy = (await db.StaffTimeOffs.AsNoTracking()
                .Where(t => staffIds.Contains(t.StaffMemberId) && t.StartAt < endUtc && t.EndAt > startUtc)
                .Select(t => new { t.StaffMemberId, t.StartAt, t.EndAt })
                .ToListAsync(ct))
            .Select(t => (t.StaffMemberId, t.StartAt, t.EndAt))
            .ToList();
        if (includeAppointments)
        {
            busy.AddRange((await db.Appointments.AsNoTracking()
                    .Where(a => staffIds.Contains(a.StaffMemberId) && a.Status != AppointmentStatus.Cancelled && a.Id != excludeAppointmentId
                        && a.StartAt < endUtc && a.EndAt > startUtc)
                    .Select(a => new { a.StaffMemberId, a.StartAt, a.EndAt })
                    .ToListAsync(ct))
                .Select(a => (a.StaffMemberId, a.StartAt, a.EndAt)));
        }

        var busyByStaff = busy.ToLookup(b => b.StaffMemberId, b => (b.StartAt, b.EndAt));
        var result = new Dictionary<(Guid, DateOnly), StaffDayPlan>();
        foreach (var staffId in staffIds)
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var business = week.GetValueOrDefault(day.DayOfWeek);
                var (dayStart, dayEnd) = BusinessTime.DayRangeUtc(day);
                result[(staffId, day)] = new StaffDayPlan(
                    day,
                    business is null || business.IsClosed,
                    business?.OpensAt ?? TimeOnly.MinValue,
                    business?.ClosesAt ?? TimeOnly.MinValue,
                    holidays.GetValueOrDefault(day),
                    hours[(staffId, day.DayOfWeek)].Select(h => (h.StartsAt, h.EndsAt)).ToList(),
                    busyByStaff[staffId].Where(b => b.StartAt < dayEnd && b.EndAt > dayStart).ToList());
            }
        }

        return result;
    }
}
