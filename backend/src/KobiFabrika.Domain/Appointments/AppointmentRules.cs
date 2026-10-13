namespace KobiFabrika.Domain.Appointments;

/// <summary>Randevu durum geçişleri, süre sınırları ve çakışma kuralı.</summary>
public static class AppointmentRules
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 720;

    /// <summary>Saati dolduran durumlar. İptal saati boşa çıkarır.</summary>
    public static readonly AppointmentStatus[] BlockingStatuses =
        [AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.Completed, AppointmentStatus.NoShow];

    public static bool Blocks(AppointmentStatus status) => status != AppointmentStatus.Cancelled;

    /// <summary>
    /// İzin verilen geçişler. Beklemede → Onaylı/İptal; Onaylı → Tamamlandı/Gelmedi/İptal; Tamamlandı → Onaylı ("Geri al",
    /// satışa çevrilmemişse); Gelmedi → Onaylı ("Geri al"). İptal son durumdur.
    /// </summary>
    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to, bool hasSale) => (from, to) switch
    {
        (AppointmentStatus.Pending, AppointmentStatus.Confirmed) => true,
        (AppointmentStatus.Pending, AppointmentStatus.Cancelled) => true,
        (AppointmentStatus.Confirmed, AppointmentStatus.Completed) => true,
        (AppointmentStatus.Confirmed, AppointmentStatus.NoShow) => true,
        (AppointmentStatus.Confirmed, AppointmentStatus.Cancelled) => true,
        (AppointmentStatus.Completed, AppointmentStatus.Confirmed) => !hasSale,
        (AppointmentStatus.NoShow, AppointmentStatus.Confirmed) => true,
        _ => false,
    };

    /// <summary>Tamamlandı ve Gelmedi ancak başlangıç saati geldikten sonra işaretlenir.</summary>
    public static bool RequiresStarted(AppointmentStatus to) => to is AppointmentStatus.Completed or AppointmentStatus.NoShow;

    /// <summary>Saati, süresi, hizmeti veya personeli değiştirilebilir mi (taşıma, süre uzatma).</summary>
    public static bool IsMovable(AppointmentStatus status, bool hasSale) =>
        !hasSale && status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;

    /// <summary>[aStart, aEnd) ile [bStart, bEnd) kesişir mi (uç uca randevular çakışmaz).</summary>
    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) => aStart < bEnd && bStart < aEnd;
}

/// <summary>Gelmeyen müşteri uyarısı: son <see cref="Of"/> randevusundan <see cref="NoShows"/> tanesine gelmedi.</summary>
public sealed record NoShowWarning(int NoShows, int Of);

/// <summary>
/// Gelmeyen müşteri kuralı: başlangıcı geçmiş, Tamamlandı veya Gelmedi durumundaki son 5 randevudan 2 veya fazlasına
/// gelmediyse uyarı. İptaller ve gelecek randevular sayılmaz.
/// </summary>
public static class NoShowRule
{
    public const int Window = 5;
    public const int Threshold = 2;

    public static NoShowWarning? Evaluate(IEnumerable<(DateTime StartAt, AppointmentStatus Status)> history, DateTime nowUtc)
    {
        var last = history
            .Where(h => h.StartAt <= nowUtc && h.Status is AppointmentStatus.Completed or AppointmentStatus.NoShow)
            .OrderByDescending(h => h.StartAt)
            .Take(Window)
            .ToList();
        var noShows = last.Count(h => h.Status == AppointmentStatus.NoShow);
        return noShows >= Threshold ? new NoShowWarning(noShows, last.Count) : null;
    }
}
