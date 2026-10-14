using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Businesses;

/// <summary>İşletme (kiracının kendisi). Kiracı tablosu değildir; diğer tablolar buna BusinessId ile bağlanır.</summary>
public class Business : Entity, ISoftDeletable, IAuditable
{
    public required string Name { get; set; }

    /// <summary>Online randevu linkinde kullanılır: kobifabrika.com/r/{slug}. Benzersiz.</summary>
    public required string Slug { get; set; }

    public BusinessSector Sector { get; set; } = BusinessSector.Other;

    /// <summary>Sektörden türetilir (<see cref="SectorCatalog"/>); sektör değişince güncellenir.</summary>
    public BusinessTemplate Template { get; set; } = BusinessTemplate.General;

    /// <summary>Açık menü modülleri; ilk değer şablondan gelir, Ayarlar'dan değiştirilebilir.</summary>
    public BusinessModules Modules { get; set; } = BusinessModules.All;

    /// <summary>Kurulum sihirbazı bitince dolar. Null iken panele girilmez, /kurulum'a yönlendirilir.</summary>
    public DateTime? SetupCompletedAt { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZone { get; set; } = "Europe/Istanbul";
    /// <summary>Tahsilat linkinde gösterilen TR IBAN (boşluksuz, büyük harf).</summary>
    public string? Iban { get; set; }

    /// <summary>IBAN'ın hesap sahibi (havale alıcı adı); boşsa işletme adı gösterilir.</summary>
    public string? IbanHolderName { get; set; }
    public string? GoogleReviewUrl { get; set; }
    public string? WhatsappNumber { get; set; }

    /// <summary>İşletme ayarları (JSON). Yapısı ilgili sprintlerde netleşecek.</summary>
    public string? Settings { get; set; }

    public int SmsBalance { get; set; }

    /// <summary>Fiş yazıcısının kağıt genişliği (mm): 58 veya 80. Fiş sayfası ve fiş PDF'i bu genişlikte.</summary>
    public int ReceiptWidth { get; set; } = DefaultReceiptWidth;

    public const int DefaultReceiptWidth = 80;

    public static readonly int[] ReceiptWidths = [58, 80];

    /// <summary>Veresiye satışta varsayılan vade (gün); satış ekranında değiştirilebilir.</summary>
    public int DefaultDueDays { get; set; } = DefaultDueDaysValue;

    public const int DefaultDueDaysValue = 30;
    public const int MaxDueDays = 365;

    /// <summary>Her sabah 09:00'da vadesi gelen alacaklar için uygulama içi bildirim.</summary>
    public bool DueDigestEnabled { get; set; } = true;

    /// <summary>Vade bildiriminin son üretildiği yerel gün (aynı gün ikinci bildirim olmaz).</summary>
    public DateOnly? LastDueDigestOn { get; set; }

    /// <summary>Personel rolü takvimde sadece kendi (bağlı olduğu personelin) randevularını görür ve yazar.</summary>
    public bool StaffSeesOnlyOwnAppointments { get; set; }

    /// <summary>Online randevu sayfası (/r/[slug]) açık mı; varsayılan kapalı, Ayarlar → Randevu'dan açılır.</summary>
    public bool BookingEnabled { get; set; }

    /// <summary>Online randevu kendiliğinden onaylanır; kapalıysa Beklemede düşer.</summary>
    public bool BookingAutoConfirm { get; set; }

    /// <summary>Online randevu en az bu kadar dakika sonrasına alınır.</summary>
    public int BookingMinNoticeMinutes { get; set; } = 120;

    /// <summary>Online randevu en fazla bu kadar gün ilerisine alınır (1-180).</summary>
    public int BookingMaxDaysAhead { get; set; } = 30;

    /// <summary>Online sayfadaki saat aralığı (dakika): 5, 10, 15, 20, 30 veya 60.</summary>
    public int BookingSlotMinutes { get; set; } = 15;

    /// <summary>Hatırlatma SMS'i randevudan kaç saat önce (0 = kapalı, 1-72).</summary>
    public int ReminderHoursBefore { get; set; } = 24;

    /// <summary>Müşteri randevusunu en az kaç saat kala iptal edebilir (0 = müşteri iptal edemez).</summary>
    public int CustomerCancelHours { get; set; } = 2;

    public static readonly int[] BookingSlotOptions = [5, 10, 15, 20, 30, 60];
    public const int MaxBookingDaysAhead = 180;
    public const int MaxBookingMinNoticeMinutes = 7 * 24 * 60;
    public const int MaxReminderHoursBefore = 72;
    public const int MaxCustomerCancelHours = 72;

    /// <summary>Sektörü ve ona bağlı şablonu ayarlar. Kurulumda modüller de şablonun varsayılanına döner.</summary>
    public void SetSector(BusinessSector sector, bool resetModules)
    {
        Sector = sector;
        Template = SectorCatalog.TemplateFor(sector);
        if (resetModules)
        {
            Modules = SectorCatalog.DefaultModulesFor(Template);
        }
    }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
