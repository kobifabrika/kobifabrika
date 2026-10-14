using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KobiFabrika.Domain.Messaging;

namespace KobiFabrika.Domain.Marketing;

/// <summary>
/// Pazarlama mesajı şablonu: <c>{ad}</c> (müşterinin adı; yoksa görünen adı) ve <c>{isletme}</c> değişkenleri.
/// Her mesajın sonuna çıkış eki gelir (<see cref="OptOut"/>).
/// </summary>
public static partial class MessageTemplate
{
    public const string FirstNameVariable = "{ad}";
    public const string BusinessVariable = "{isletme}";

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex VariablePattern();

    /// <summary>Şablondaki bilinmeyen değişkenler ({soyad} gibi). Boşsa şablon geçerli.</summary>
    public static IReadOnlyList<string> UnknownVariables(string body) =>
        VariablePattern().Matches(body).Select(m => m.Value)
            .Where(v => v is not FirstNameVariable and not BusinessVariable)
            .Distinct()
            .ToList();

    public static string Render(string body, string? firstName, string displayName, string businessName)
    {
        var name = string.IsNullOrWhiteSpace(firstName) ? displayName.Trim() : firstName.Trim();
        return body.Replace(FirstNameVariable, name, StringComparison.Ordinal)
            .Replace(BusinessVariable, businessName.Trim(), StringComparison.Ordinal)
            .Trim();
    }

    /// <summary>Gövde + boşluk + çıkış eki.</summary>
    public static string WithFooter(string rendered, string footer) => rendered + " " + footer;
}

/// <summary>
/// Çıkış (İYS ret) eki ve müşterinin kısa çıkış kodu. Ek sadece ASCII'dir: Türkçe karakter içermediği için tek başına mesajı
/// Unicode'a (70 karakter/segment) zorlamaz. Örnek: "SMS almamak icin: kobifabrika.com/c/Ab12Cd34 veya RET".
/// Link kökü ayardan (PublicLinks:ShortBaseUrl) gelir; segment sayısı bu uzunlukla hesaplanır.
/// </summary>
public static class OptOut
{
    public const int CodeLength = 8;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>8 karakter (karışabilen 0/O, 1/l/I yok; ~46 bit). Kod tahmin edilemez; uç IP başına sınırlı.</summary>
    public static string NewCode() => RandomNumberGenerator.GetString(Alphabet, CodeLength);

    public static bool LooksValid(string? code) => code is { Length: CodeLength } && code.All(c => Alphabet.Contains(c));

    public static string Link(string shortBaseUrl, string code) => $"{shortBaseUrl.TrimEnd('/')}/c/{code}";

    /// <summary>Çıkış eki. <paramref name="keyword"/>: "RET" veya sağlayıcı gelince "RET yazip 3XXX'e gonderin".</summary>
    public static string Footer(string shortBaseUrl, string code, string keyword) =>
        $"SMS almamak icin: {Link(shortBaseUrl, code)} veya {keyword}";

    /// <summary>Kod henüz yokken uzunluk hesabı için örnek ek (aynı uzunlukta).</summary>
    public static string SampleFooter(string shortBaseUrl, string keyword) => Footer(shortBaseUrl, new string('X', CodeLength), keyword);
}

/// <summary>Bir pazarlama alıcısının uygunluğu (izin, çıkış, telefon). Uygunsa null.</summary>
public static class MarketingEligibility
{
    /// <param name="hasRevokedSms">SMS izni bir kez geri alınmış mı (izin geçmişinde Granted=false satırı).</param>
    public static RecipientSkipReason? Check(string? phone, bool smsConsent, bool hasRevokedSms)
    {
        if (phone is null || !IsTrMobile(phone))
        {
            return RecipientSkipReason.NoPhone;
        }

        if (!smsConsent)
        {
            return hasRevokedSms ? RecipientSkipReason.OptedOut : RecipientSkipReason.NoConsent;
        }

        return null;
    }

    public static bool IsTrMobile(string e164) => e164.StartsWith("+905", StringComparison.Ordinal) && e164.Length == 13;

    /// <summary>Segment sayısı (çıkış eki dahil).</summary>
    public static int Segments(string fullText) => SmsSegments.Measure(fullText).Segments;
}
