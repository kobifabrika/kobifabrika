using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Marketing;

/// <summary>Kredi hareketinin türü.</summary>
public enum SmsCreditKind
{
    /// <summary>Platformun yüklediği kredi (Sprint 8'e kadar sadece PlatformAdmin; sonra iyzico).</summary>
    TopUp,

    /// <summary>Kurulum tamamlanınca bir kez verilen hoş geldin kredisi.</summary>
    Welcome,

    /// <summary>Gönderilen SMS (eksi, segment sayısı kadar).</summary>
    Send,

    /// <summary>Sağlayıcı hatasında gönderimin iadesi (artı, bir kez).</summary>
    Refund,

    /// <summary>Platformun elle düzeltmesi.</summary>
    Adjustment,
}

/// <summary>
/// İşletmenin SMS kredisi hareketi (sadece eklenir). Bakiye <c>Businesses.SmsBalance</c>'ta önbellek; sadece SmsCredits
/// servisindeki atomik UPDATE ile değişir ve eksiye düşemez (CHECK). Σ Amount = bakiye.
/// </summary>
public class SmsCreditTransaction : TenantEntity
{
    public const int MaxNoteLength = 200;

    /// <summary>Artı: yükleme/iade; eksi: gönderim.</summary>
    public int Amount { get; set; }

    public SmsCreditKind Kind { get; set; }

    /// <summary>Hareketten sonraki bakiye (UPDATE … OUTPUT ile aynı anda okunur).</summary>
    public int BalanceAfter { get; set; }

    public Guid? MessageLogId { get; set; }
    public string? Note { get; set; }
}

/// <summary>SMS kredisi kuralları (1 kredi = 1 SMS segmenti).</summary>
public static class SmsCredit
{
    public const int CreditPerSegment = 1;
    public const int WelcomeCredits = 50;
    public const int DefaultLowBalanceThreshold = 50;
    public const int MaxLowBalanceThreshold = 100_000;
    public const int MaxTopUp = 1_000_000;

    public static int Cost(int segments) => segments * CreditPerSegment;

    /// <summary>Düşümden sonra eşiğin altına inildi mi (eşik 0 = uyarı kapalı).</summary>
    public static bool CrossedLowThreshold(int before, int after, int threshold) =>
        threshold > 0 && before >= threshold && after < threshold;
}
