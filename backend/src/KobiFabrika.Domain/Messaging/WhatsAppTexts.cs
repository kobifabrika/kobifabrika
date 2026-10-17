namespace KobiFabrika.Domain.Messaging;

/// <summary>Panelden WhatsApp ile müşteriye gönderilecek hazır mesajın türü.</summary>
public enum WhatsAppNoticeKind
{
    Confirmed,
    Reminder,
    Moved,
    Cancelled,
    Rejected,
    Survey,
}

/// <summary>
/// Panelden wa.me ile açılan hazır Türkçe mesajlar (Sprint 8, SMS'siz pilot). Gönderimi kullanıcı kendi WhatsApp'ından yapar;
/// metin müşterinin adıyla başlar, işletme adı, gün/saat, hizmet ve gerekiyorsa iptal/randevu linki içerir.
/// </summary>
public static class WhatsAppTexts
{
    public static string Appointment(
        WhatsAppNoticeKind kind, string firstName, string business, DateTime startUtc, DateTime nowUtc, string service,
        string? manageLink, string? businessPhone, string? bookingLink)
    {
        var hello = string.IsNullOrWhiteSpace(firstName) ? "Merhaba" : $"Merhaba {firstName.Trim()}";
        var when = SmsTexts.When(startUtc);
        return kind switch
        {
            WhatsAppNoticeKind.Confirmed =>
                $"{hello}, {business} randevunuz onaylandı: {when}, {service}." + Manage(manageLink),
            WhatsAppNoticeKind.Reminder =>
                $"{hello}, {business} olarak {SmsTexts.Relative(startUtc, nowUtc)} {service} randevunuzu hatırlatırız." + Manage(manageLink),
            WhatsAppNoticeKind.Moved =>
                $"{hello}, {business} randevunuz {when} olarak değişti ({service})." + Manage(manageLink),
            WhatsAppNoticeKind.Cancelled =>
                $"{hello}, {when} tarihli {service} randevunuz iptal edildi." + (businessPhone is null ? "" : $" Bilgi için: {businessPhone}")
                + (bookingLink is null ? "" : $" Yeni randevu: {bookingLink}"),
            WhatsAppNoticeKind.Rejected =>
                $"{hello}, {business} olarak {when} için randevu talebinizi maalesef onaylayamadık."
                + (bookingLink is null ? (businessPhone is null ? "" : $" Bilgi için: {businessPhone}") : $" Başka bir saat seçebilirsiniz: {bookingLink}"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public static string Survey(string firstName, string business, string surveyLink)
    {
        var hello = string.IsNullOrWhiteSpace(firstName) ? "Merhaba" : $"Merhaba {firstName.Trim()}";
        return $"{hello}, bizi tercih ettiğiniz için teşekkür ederiz. {business} hizmetini 1'den 5'e kadar puanlar mısınız? {surveyLink}";
    }

    private static string Manage(string? link) => link is null ? "" : $" Randevunuzu görmek veya iptal etmek için: {link}";

    /// <summary>wa.me linki: +905321234567 → https://wa.me/905321234567?text=…</summary>
    public static string Link(string e164, string text) =>
        $"https://wa.me/{e164.TrimStart('+')}?text={Uri.EscapeDataString(text)}";
}
