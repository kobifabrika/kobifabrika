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
