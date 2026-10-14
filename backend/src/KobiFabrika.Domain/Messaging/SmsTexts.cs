using System.Globalization;
using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Messaging;

/// <summary>
/// Müşteriye giden SMS metinleri. Doğrulama kodu tek segmente sığar (işletme adı gerekirse kısaltılır); diğerleri bilgilendirme
/// (İYS/KVKK pazarlama izni aranmaz) ve birden fazla segment olabilir.
/// </summary>
public static class SmsTexts
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"{İşletme} randevu kodunuz: 123456". Tek segment: Türkçe karakterde 70, değilse 160 karakter; uzun ad kısaltılır.</summary>
    public static string Verification(string businessName, string code)
    {
        var suffix = $" randevu kodunuz: {code}";
        var name = businessName.Trim();
        var text = name + suffix;
        while (name.Length > 0 && SmsSegments.Measure(text).Segments > 1)
        {
            name = name[..^1].TrimEnd();
            text = name + suffix;
        }

        return name.Length == 0 ? suffix.TrimStart() : text;
    }

    /// <summary>"15 Ekim Çarşamba 14:30" (İstanbul saati).</summary>
    public static string When(DateTime startUtc) => BusinessTime.ToLocal(startUtc).ToString("d MMMM dddd HH:mm", Tr);

    /// <summary>Hatırlatmada gün: "Bugün 14:30", "Yarın 14:30" veya "17 Ekim Cuma 14:30".</summary>
    public static string Relative(DateTime startUtc, DateTime nowUtc)
    {
        var local = BusinessTime.ToLocal(startUtc);
        var days = BusinessTime.LocalDay(startUtc).DayNumber - BusinessTime.LocalDay(nowUtc).DayNumber;
        return days switch
        {
            0 => $"Bugün {local:HH:mm}",
            1 => $"Yarın {local:HH:mm}",
            _ => When(startUtc),
        };
    }

    public static string Booked(string business, DateTime startUtc, string service, bool confirmed, string link) => confirmed
        ? $"{business}: {When(startUtc)} {service} randevunuz onaylandı. İptal: {link}"
        : $"{business}: {When(startUtc)} {service} randevu talebiniz alındı; onaylanınca SMS ile bildireceğiz. İptal: {link}";

    public static string Confirmed(string business, DateTime startUtc, string service, string link) =>
        $"{business}: {When(startUtc)} {service} randevunuz onaylandı. İptal: {link}";

    public static string Reminder(string business, DateTime startUtc, DateTime nowUtc, string service, string link) =>
        $"{business}: {Relative(startUtc, nowUtc)} {service} randevunuzu hatırlatırız. İptal: {link}";

    public static string Moved(string business, DateTime startUtc, string service, string link) =>
        $"{business}: {service} randevunuz {When(startUtc)} olarak değişti. İptal: {link}";

    public static string Cancelled(string business, DateTime startUtc, string service, string? phone) =>
        $"{business}: {When(startUtc)} {service} randevunuz iptal edildi." + (phone is null ? "" : $" Bilgi: {phone}");

    public static string Rejected(string business, DateTime startUtc, string service, string? phone) =>
        $"{business}: {When(startUtc)} {service} randevu talebiniz onaylanmadı." + (phone is null ? "" : $" Bilgi: {phone}");
}
