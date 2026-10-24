using System.Text.RegularExpressions;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Sentry'ye giden olaylardan kişisel veriyi ayıklar (karar günlüğü 2026-10-17, 2026-10-24): e-posta adresinin yerel kısmı ve
/// telefon numarasının son iki hanesi dışı maskelenir; IP, çerez ve istek gövdesi zaten gitmez (SendDefaultPii kapalı).
/// Mesaj, istisna metni, kayıt izleri (breadcrumb), ek alanlar ve sorgu dizesi taranır.
/// </summary>
public static partial class SentryScrubber
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@([A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,})")]
    private static partial Regex EmailPattern();

    // +90 532 123 45 67, 0532 123 4567, 5321234567, +905321234567; genel E.164 (+ ve 10-14 hane).
    [GeneratedRegex(@"(?<![\d+])(?:(?:\+?90[\s\-]?)?0?5\d{2}[\s\-]?\d{3}[\s\-]?\d{2}[\s\-]?(\d{2})|\+\d{8,12}(\d{2}))(?!\d)")]
    private static partial Regex PhonePattern();

    public static string? Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = EmailPattern().Replace(text, m => "***@" + m.Groups[1].Value);
        return PhonePattern().Replace(text, m => "[telefon **" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) + "]");
    }

    public static SentryEvent Scrub(SentryEvent sentryEvent)
    {
        sentryEvent.User = new SentryUser();
        if (sentryEvent.Message is { } message)
        {
            message.Message = Mask(message.Message);
            message.Formatted = Mask(message.Formatted);
        }

        foreach (var exception in sentryEvent.SentryExceptions ?? [])
        {
            exception.Value = Mask(exception.Value);
        }

        foreach (var (key, value) in sentryEvent.Extra.ToList())
        {
            if (value is string text)
            {
                sentryEvent.SetExtra(key, Mask(text));
            }
        }

        sentryEvent.Request.QueryString = Mask(sentryEvent.Request.QueryString);
        sentryEvent.Request.Url = Mask(sentryEvent.Request.Url);
        return sentryEvent;
    }

    public static Breadcrumb Scrub(Breadcrumb breadcrumb) =>
        new(
            Mask(breadcrumb.Message) ?? "",
            breadcrumb.Type ?? "default",
            breadcrumb.Data?.ToDictionary(pair => pair.Key, pair => Mask(pair.Value) ?? ""),
            breadcrumb.Category,
            breadcrumb.Level);
}
