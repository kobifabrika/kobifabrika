using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Messaging;

namespace KobiFabrika.Domain.Marketing;

public enum AutomationType
{
    /// <summary>Doğum günü mesajı (pazarlama; günlük, SendAtLocal'de).</summary>
    Birthday,

    /// <summary>"X gündür gelmedi" geri kazanma (pazarlama; günlük; aynı müşteriye en fazla Y günde bir).</summary>
    WinBack,

    /// <summary>Randevu sonrası teşekkür (pazarlama; tamamlanan randevudan DelayMinutes sonra).</summary>
    VisitThanks,

    /// <summary>Memnuniyet anketi linki (pazarlama; tamamlanan randevudan DelayMinutes sonra; teşekkürle aynı SMS'te birleşir).</summary>
    Survey,

    /// <summary>Vade günü hatırlatması (bilgilendirme; izin aranmaz, günlük üst sınıra girmez).</summary>
    DueReminder,
}

/// <summary>
/// İşletmenin hazır otomasyonu (Kampanyalar → Otomasyonlar). Satır yoksa varsayılan kapalı değerler gösterilir; ilk kayıtta oluşur.
/// Günlük otomasyonlar (doğum günü, geri kazanma, vade) SendAtLocal'de bir kez (LastRunOn koşullu UPDATE); ziyaret sonrası
/// otomasyonlar (teşekkür, anket) tamamlanan randevudan DelayMinutes sonra. Aynı kişiye aynı anahtarla bir kez (AutomationSends).
/// </summary>
public class Automation : TenantEntity
{
    public const int MaxBodyLength = 400;

    public AutomationType Type { get; set; }
    public bool IsEnabled { get; set; }
    public required string Body { get; set; }

    /// <summary>Günlük otomasyonların gönderim saati (İstanbul).</summary>
    public TimeOnly SendAtLocal { get; set; }

    /// <summary>Geri kazanma: son ziyaretten bu kadar gün sonra (X).</summary>
    public int? InactiveDays { get; set; }

    /// <summary>Geri kazanma: aynı müşteriye en fazla bu kadar günde bir (Y).</summary>
    public int? RepeatDays { get; set; }

    /// <summary>Teşekkür/anket: randevu tamamlandıktan bu kadar dakika sonra.</summary>
    public int? DelayMinutes { get; set; }

    /// <summary>Son açılış: ziyaret sonrası otomasyonlar bundan önce tamamlanan randevulara gitmez.</summary>
    public DateTime? EnabledAt { get; set; }

    /// <summary>Günlük otomasyonun son çalıştığı İstanbul günü (aynı gün ikinci tur olmaz).</summary>
    public DateOnly? LastRunOn { get; set; }

    public bool IsDaily => Type is AutomationType.Birthday or AutomationType.WinBack or AutomationType.DueReminder;

    public bool IsAfterVisit => Type is AutomationType.VisitThanks or AutomationType.Survey;

    public MessagePurpose Purpose => Type switch
    {
        AutomationType.Birthday => MessagePurpose.Birthday,
        AutomationType.WinBack => MessagePurpose.WinBack,
        AutomationType.VisitThanks => MessagePurpose.VisitThanks,
        AutomationType.Survey => MessagePurpose.Survey,
        _ => MessagePurpose.DueReminder,
    };
}

/// <summary>Bir otomasyonun bir kişiye gönderimi; (işletme, tür, kişi, anahtar) benzersiz → çift gönderim olmaz.</summary>
public class AutomationSend : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public AutomationType Type { get; set; }
    public Guid ContactId { get; set; }

    /// <summary>Doğum günü: yıl ("2026"); geri kazanma ve vade: gün ("2026-10-15"); teşekkür/anket: randevu Id'si.</summary>
    public required string Key { get; set; }

    public Guid? MessageLogId { get; set; }

    /// <summary>Gönderilmedi (izin yok, günlük sınır, kredi…): neden (kayıt yine tutulur, tekrar denenmez).</summary>
    public string? SkipReason { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Otomasyonların varsayılanları (satır yokken gösterilir; hepsi kapalı başlar).</summary>
public static class AutomationDefaults
{
    public const string SurveyLinkVariable = "{link}";

    public static Automation For(AutomationType type) => type switch
    {
        AutomationType.Birthday => new Automation
        {
            Type = type, Body = "{isletme}: İyi ki doğdunuz {ad}! Bu ay size özel indirimimiz var, bekleriz.",
            SendAtLocal = new TimeOnly(10, 0),
        },
        AutomationType.WinBack => new Automation
        {
            Type = type, Body = "{ad}, sizi özledik! {isletme}'de bir sonraki ziyaretinizde %10 indirim sizi bekliyor.",
            SendAtLocal = new TimeOnly(11, 0), InactiveDays = 60, RepeatDays = 90,
        },
        AutomationType.VisitThanks => new Automation
        {
            Type = type, Body = "{ad}, bugün {isletme}'yi tercih ettiğiniz için teşekkür ederiz.", DelayMinutes = 120,
        },
        AutomationType.Survey => new Automation
        {
            Type = type, Body = "{ad}, {isletme} ziyaretinizi 1-5 arası puanlar mısınız? {link}", DelayMinutes = 120,
        },
        _ => new Automation
        {
            Type = type, Body = "{isletme}: Merhaba {ad}, bugün vadesi gelen {tutar} ödemeniz bulunuyor. {link}",
            SendAtLocal = new TimeOnly(10, 0),
        },
    };

    public const string AmountVariable = "{tutar}";

    /// <summary>Türün şablonunda kullanılabilen değişkenler ({ad} ve {isletme} her türde).</summary>
    public static IReadOnlyList<string> Variables(AutomationType type) => type switch
    {
        AutomationType.Survey => [MessageTemplate.FirstNameVariable, MessageTemplate.BusinessVariable, SurveyLinkVariable],
        AutomationType.DueReminder => [MessageTemplate.FirstNameVariable, MessageTemplate.BusinessVariable, AmountVariable, SurveyLinkVariable],
        _ => [MessageTemplate.FirstNameVariable, MessageTemplate.BusinessVariable],
    };

    public static readonly AutomationType[] All =
        [AutomationType.Birthday, AutomationType.WinBack, AutomationType.VisitThanks, AutomationType.Survey, AutomationType.DueReminder];
}

/// <summary>Otomasyon kuralları (saf fonksiyonlar, birim testli).</summary>
public static class AutomationRules
{
    /// <summary>Bugün doğum günü mü? 29 Şubat doğumlular artık yıl olmayan yıllarda 28 Şubat'ta kutlanır.</summary>
    public static bool IsBirthday(DateOnly birthDate, DateOnly today)
    {
        if (birthDate.Month == 2 && birthDate.Day == 29 && !DateTime.IsLeapYear(today.Year))
        {
            return today.Month == 2 && today.Day == 28;
        }

        return birthDate.Month == today.Month && birthDate.Day == today.Day;
    }

    /// <summary>
    /// Geri kazanma: son ziyaretten en az X gün geçmiş ve (hiç gönderilmemiş ya da son gönderimden en az Y gün geçmiş).
    /// Son gönderimden sonra yeni ziyaret olduysa Y beklenmez (müşteri geri gelmiş, yeni döngü).
    /// </summary>
    public static bool WinBackDue(DateOnly lastVisit, DateOnly? lastSent, DateOnly today, int inactiveDays, int repeatDays)
    {
        if (today.DayNumber - lastVisit.DayNumber < inactiveDays)
        {
            return false;
        }

        return lastSent is not { } sent || sent < lastVisit || today.DayNumber - sent.DayNumber >= repeatDays;
    }

    /// <summary>Teşekkür ve anket birleşik metni: teşekkür + anket cümlesi (anket metninden {ad}/{isletme} çıkmadan).</summary>
    public static string CombineThanksAndSurvey(string thanksBody, string surveyBody) => thanksBody.TrimEnd() + " " + surveyBody.Trim();
}

/// <summary>Ziyaret istatistiği: aynı İstanbul gününde satış + randevu tek ziyarettir.</summary>
public readonly record struct VisitStats(int Count, DateOnly? First, DateOnly? Last, decimal? AvgIntervalDays)
{
    public static VisitStats From(IEnumerable<DateOnly> visitDays)
    {
        var days = visitDays.Distinct().Order().ToList();
        if (days.Count == 0)
        {
            return new VisitStats(0, null, null, null);
        }

        decimal? avg = days.Count > 1 ? Math.Round((decimal)(days[^1].DayNumber - days[0].DayNumber) / (days.Count - 1), 2) : null;
        return new VisitStats(days.Count, days[0], days[^1], avg);
    }
}

/// <summary>Kaybedilme riski: düzenli müşteri (en az 3 ziyaret) ve son ziyaretten bu yana max(2 × ortalama aralık, 14 gün)'den fazla.</summary>
public static class RiskRule
{
    public const int MinVisits = 3;
    public const int MinThresholdDays = 14;

    public static decimal ThresholdDays(decimal avgIntervalDays) => Math.Max(2 * avgIntervalDays, MinThresholdDays);

    public static bool IsAtRisk(int visitCount, decimal? avgIntervalDays, DateOnly? lastVisit, DateOnly today) =>
        visitCount >= MinVisits && avgIntervalDays is { } avg && lastVisit is { } last && today.DayNumber - last.DayNumber > ThresholdDays(avg);
}

/// <summary>Anket sonuç sayfasında neyin öne çıkacağı (Google "review gating" yasağı: link herkese gösterilir).</summary>
public enum SurveyEmphasis
{
    /// <summary>1-3: "Bize iletin" yorum kutusu önde, Google linki altta sade.</summary>
    FeedbackFirst,

    /// <summary>4-5: Google'da yorum düğmesi önde.</summary>
    GoogleFirst,
}

public static class SurveyRules
{
    public const int CodeLength = 10;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>Puandan sonra yorum bu süre içinde yazılabilir.</summary>
    public static readonly TimeSpan CommentWindow = TimeSpan.FromHours(24);

    public static SurveyEmphasis Emphasis(int score) => score <= 3 ? SurveyEmphasis.FeedbackFirst : SurveyEmphasis.GoogleFirst;

    public static bool CanComment(int? score) => score is >= 1 and <= 3;

    /// <summary>10 karakter (karışabilen harfler yok; ~57 bit). Veritabanında SHA-256 özeti.</summary>
    public static string NewCode() =>
        System.Security.Cryptography.RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", CodeLength);

    public static bool LooksValid(string? code) => code is { Length: CodeLength } && code.All(char.IsAsciiLetterOrDigit);
}

/// <summary>Boş saat önerisi: personel başına en fazla 2, toplam en fazla 3 aralık; en uzun aralıklar önce, sonra saat sırası.</summary>
public static class EmptySlotRules
{
    public const int PerStaff = 2;
    public const int Total = 3;

    public static IReadOnlyList<(T Staff, int Start, int End)> Pick<T>(IEnumerable<(T Staff, int Start, int End)> ranges, int minMinutes)
    {
        var candidates = ranges.Where(r => r.End - r.Start >= minMinutes).ToList();
        var perStaff = candidates.GroupBy(r => r.Staff)
            .SelectMany(g => g.OrderByDescending(r => r.End - r.Start).ThenBy(r => r.Start).Take(PerStaff));
        return perStaff.OrderByDescending(r => r.End - r.Start).ThenBy(r => r.Start).Take(Total)
            .OrderBy(r => r.Start).ToList();
    }
}
