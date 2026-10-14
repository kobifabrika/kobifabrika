using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Appointments;

public enum AppointmentStatus
{
    /// <summary>Beklemede (online randevuda işletmenin onayını bekler).</summary>
    Pending,

    /// <summary>Onaylı.</summary>
    Confirmed,

    /// <summary>Tamamlandı (satışa çevrilebilir).</summary>
    Completed,

    /// <summary>Gelmedi.</summary>
    NoShow,

    /// <summary>İptal (son durum; saat boşa çıkar).</summary>
    Cancelled,
}

public enum AppointmentSource
{
    Panel,
    Online,
}

public enum AppointmentCancelSource
{
    Business,
    Customer,
}

/// <summary>Randevu. Saatler UTC; çakışma kontrolü sunucuda personel satırı kilitlenerek yapılır (AppointmentBooking).</summary>
public class Appointment : TenantEntity
{
    public const int MaxNoteLength = 500;

    public Guid ContactId { get; set; }
    public Guid StaffMemberId { get; set; }

    /// <summary>Hizmet (Products.Type = Service).</summary>
    public Guid ProductId { get; set; }

    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public AppointmentSource Source { get; set; } = AppointmentSource.Panel;
    public string? Note { get; set; }

    /// <summary>Satışa çevrildiyse satış; satış iptal edilince temizlenir (randevu yeniden çevrilebilir).</summary>
    public Guid? SaleId { get; set; }

    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? NoShowAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public AppointmentCancelSource? CancelSource { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>Saatin en son belirlendiği an (oluşturma veya taşıma). Hatırlatma kuralı: kısa süre kala alınan randevuya hatırlatma gitmez.</summary>
    public DateTime TimeSetAt { get; set; }

    /// <summary>Hatırlatma SMS'i gönderildi (bir kez; taşınınca temizlenir).</summary>
    public DateTime? ReminderSentAt { get; set; }

    /// <summary>Paketten kullanılan randevu: Tamamlandı'da seans düşer (<see cref="PackageSessionUsed"/>), geri alınca iade edilir.</summary>
    public Guid? CustomerPackageId { get; set; }

    public bool PackageSessionUsed { get; set; }

    /// <summary>Müşterinin randevu sayfası (/r/[slug]/randevu/[token]) linkinin SHA-256 özeti; filtreli benzersiz.</summary>
    public byte[]? ManageTokenHash { get; set; }

    /// <summary>Aynı linki SMS'lerde yeniden kullanmak için Data Protection ile şifrelenmiş token.</summary>
    public string? ManageToken { get; set; }
}

/// <summary>İşletmenin tatil / kapalı günü (elle eklenir). O gün online randevu alınmaz, takvimde kapalı görünür.</summary>
public class Holiday : TenantEntity
{
    public const int MaxNameLength = 100;

    public DateOnly Date { get; set; }
    public required string Name { get; set; }
}
