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
/// Personel (takvim kolonu). Sprint 5'te takvim için gereken alanlar; işe başlama, maaş, mesai ve izin Sprint 7'de eklenir.
/// Bağlı kullanıcı hesabı isteğe bağlı: Personel rolündeki kullanıcı "kendi randevularını" bu bağla görür.
/// </summary>
public class StaffMember : TenantEntity
{
    public const int MaxNameLength = 100;

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

    /// <summary>İzin (Sprint 7'de onaylanan izinler buraya yazılacak).</summary>
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
}
