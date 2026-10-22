using System.Globalization;

namespace KobiFabrika.Domain.Common;

/// <summary>
/// Para tutarı kuralları (frontend src/lib/validation/rules.ts ile aynı): boş geçilemez, 0'dan büyük,
/// en fazla 2 ondalık, decimal(18,2) sınırında. Tutarın yönü (borç/alacak) ayrı alanla verilir.
/// </summary>
public static class Money
{
    /// <summary>decimal(18,2)'nin üst sınırına göre güvenli üst sınır (1 trilyon TL'nin altı).</summary>
    public const decimal Max = 999_999_999_999.99m;

    public const string RequiredMessage = "Tutar girin.";
    public const string PositiveMessage = "Tutar 0'dan büyük olmalı.";
    public const string DecimalsMessage = "Tutar en fazla 2 ondalık basamak içerebilir.";
    public const string MaxMessage = "Tutar en fazla 999.999.999.999,99 TL olabilir.";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Bütün tutar metinleri buradan (PDF, e-posta, SMS, bildirim, hata mesajı; frontend src/lib/format.ts formatMoney ile aynı kural,
    /// karar 2026-10-22): binlik nokta, kuruş varsa virgülle iki hane, TL'den önce bölünmeyen boşluk. 1299 → "1.299 TL", 1250,5 → "1.250,50 TL".
    /// </summary>
    public static string Format(decimal value)
    {
        var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0m; // -0,00 → 0
        return rounded.ToString(rounded == decimal.Truncate(rounded) ? "#,##0" : "#,##0.00", Tr) + "\u00A0TL";
    }

    public static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, 2) == value;

    /// <summary>Kurala uymuyorsa Türkçe hata mesajı, uyuyorsa null.</summary>
    public static string? Validate(decimal? value) => value switch
    {
        null => RequiredMessage,
        <= 0 => PositiveMessage,
        > Max => MaxMessage,
        { } v when !HasAtMostTwoDecimals(v) => DecimalsMessage,
        _ => null,
    };
}
