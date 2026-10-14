namespace KobiFabrika.Domain.Messaging;

/// <summary>SMS'in kaç parça (segment) gideceği. Unicode: GSM-7 dışı karakter var (ör. ş, ğ, ı, İ, ç), mesaj UCS-2 gider.</summary>
public readonly record struct SmsSize(int Length, int Segments, bool Unicode);

/// <summary>
/// SMS segment hesabı (GSM 03.38). GSM-7: tek parça 160, çok parçada parça başına 153 karakter; genişletilmiş karakterler
/// (^ { } \ [ ~ ] | €) 2 sayılır. Türkçe karakter (ş, ğ, ı, İ, Ş, Ğ, ç) varsa mesaj UCS-2 gider: tek parça 70, çok parçada 67.
/// </summary>
public static class SmsSegments
{
    public const int GsmSingle = 160;
    public const int GsmMulti = 153;
    public const int UnicodeSingle = 70;
    public const int UnicodeMulti = 67;

    private const string Basic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    private const string Extended = "^{}\\[~]|€\f";

    private static readonly HashSet<char> BasicSet = [.. Basic];
    private static readonly HashSet<char> ExtendedSet = [.. Extended];

    public static bool IsGsm7(string text) => text.All(c => BasicSet.Contains(c) || ExtendedSet.Contains(c));

    public static SmsSize Measure(string text)
    {
        if (text.Length == 0)
        {
            return new SmsSize(0, 0, false);
        }

        if (IsGsm7(text))
        {
            var septets = text.Sum(c => ExtendedSet.Contains(c) ? 2 : 1);
            return new SmsSize(septets, septets <= GsmSingle ? 1 : (septets + GsmMulti - 1) / GsmMulti, false);
        }

        var units = text.Length;
        return new SmsSize(units, units <= UnicodeSingle ? 1 : (units + UnicodeMulti - 1) / UnicodeMulti, true);
    }
}
