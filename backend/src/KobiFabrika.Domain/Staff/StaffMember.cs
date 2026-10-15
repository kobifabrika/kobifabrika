using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Staff;

/// <summary>
/// Takvimdeki personel rengi; 3 renk kuralına uyan tonlar (docs/04-tasarim.md). Renk sadece kolon başlığında ve
/// randevu bloğunun sol şeridinde kullanılır; bloğun zemini randevunun durumunu gösterir.
/// </summary>
public enum StaffColor
{
    Black,
    DarkGray,
    Gray,
    LightGray,
    Blue,
    Outline,
}

/// <summary>
/// Personel (takvim kolonu, mesai ve izin sahibi). Bağlı kullanıcı hesabı isteğe bağlı: Personel rolündeki kullanıcı
/// "kendi randevularını", mesaisini ve izinlerini bu bağla görür. TC kimlik no saklanmaz.
/// Maaş (<see cref="Salary"/>) personel yanıtında hiç dönmez; ayrı uçtan sadece Sahip ve Mali Müşavir görür (Sprint 7).
/// </summary>
public class StaffMember : TenantEntity
{
    public const int MaxNameLength = 100;
    public const int MaxNotesLength = 1000;

    public required string FullName { get; set; }
    public string? Title { get; set; }

    /// <summary>E.164.</summary>
    public string? Phone { get; set; }

    public StaffColor Color { get; set; } = StaffColor.Black;

    /// <summary>Online randevu sayfasında seçilebilir mi.</summary>
    public bool ShowInBooking { get; set; } = true;

    /// <summary>İşletmenin aktif bir üyesinin kullanıcı Id'si (bir kullanıcı tek personele bağlanır).</summary>
    public Guid? UserId { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>İşe başlama günü (yıllık izin hakkı kıdemden hesaplanır).</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>İsteğe bağlı; yıllık izinde yaş kuralı (18 ve altı, 50 ve üstü en az 20 gün) için.</summary>
    public DateOnly? BirthDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Aylık maaş (bilgi; bordro hesabı yok). Sadece Sahip yazar, Sahip ve Mali Müşavir görür.</summary>
    public decimal? Salary { get; set; }

    public SalaryBasis? SalaryBasis { get; set; }

    /// <summary>
    /// Otomatik yıllık izin hakkının başladığı gün: işe başlama tarihi girildiği gün. Bu günden önceki yıl dönümleri
    /// otomatik yazılmaz (açılış bakiyesi elle girilir); sonrakiler <c>leave-accruals</c> işiyle eklenir.
    /// </summary>
    public DateOnly? LeaveAccrualFrom { get; set; }
}

public enum SalaryBasis
{
    Gross,
    Net,
}

/// <summary>Personelin haftalık çalışma aralığı (yerel saat). Gün başına birden çok satır olabilir (ör. öğle arası).</summary>
public class StaffWorkingHour : TenantEntity
{
    public Guid StaffMemberId { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeOnly StartsAt { get; set; }
    public TimeOnly EndsAt { get; set; }
}

/// <summary>Personelin verebildiği hizmet. Personelin hiç satırı yoksa bütün hizmetleri verir. Soft delete yok.</summary>
public class StaffMemberService : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid StaffMemberId { get; set; }
    public Guid ProductId { get; set; }
}

public enum TimeOffKind
{
    /// <summary>Mola.</summary>
    Break,

    /// <summary>İzin. Sprint 7'den itibaren onaylanan izin talepleri yazar (<see cref="StaffTimeOff.LeaveRequestId"/> dolu).</summary>
    Leave,

    Other,
}

/// <summary>Personelin kapalı zamanı (UTC). Takvimde kapalı görünür, online randevuda boş saat çıkmaz.</summary>
public class StaffTimeOff : TenantEntity
{
    public Guid StaffMemberId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public TimeOffKind Kind { get; set; }
    public string? Note { get; set; }

    /// <summary>Onaylanan izin talebinden geldiyse talep; bu kapalı zaman sadece izin iptaliyle kalkar.</summary>
    public Guid? LeaveRequestId { get; set; }
}
