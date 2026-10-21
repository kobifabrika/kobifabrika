using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using KobiFabrika.Domain.Common;

namespace KobiFabrika.Infrastructure.Email;

/// <summary>Ayrıntı tablosunun satırı (Tarih, Saat, Hizmet…).</summary>
public sealed record EmailDetail(string Label, string Value);

/// <summary>İşletme adına giden e-postanın gönderen bilgisi: başlıkta ad (ve logo), altta iletişim.</summary>
public sealed record EmailSender(string Name, string? LogoUrl = null, string? Phone = null, string? Address = null);

/// <summary>
/// E-posta şablonları (Sprint 8): sade, telefonda okunur, satır içi stilli HTML + düz metin. 560 px tek kolon, sistem fontu,
/// koyu lacivert (#000a1d) düğme, beyaz kart, ince çizgi; gölge, gradyan, emoji yok (docs/04). Resimler engellense de okunur:
/// marka metin wordmark'tır, logo varsa üstte küçük durur. İşletme adına giden e-postalarda üstte işletme adı, altta iletişimi
/// ve "KobiFabrika ile gönderildi". Sistem e-postalarında altta şirket bilgisi (<see cref="CompanyLine"/>).
/// </summary>
public static class EmailTemplates
{
    /// <summary>Türkçe karakterleri olduğu gibi bırakır; sadece HTML'e özel karakterleri kodlar.</summary>
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Sistem e-postalarının alt bilgisi; açılışta <c>Company:*</c> ayarından (şirket kurulana kadar sadece "KobiFabrika").</summary>
    public static string CompanyLine { get; set; } = "KobiFabrika · destek@kobifabrika.com";

    /// <summary>
    /// Sistem e-postalarının üstündeki logo (siyah zeminli PNG, 200 px gösterim). Görseller engellenirse alt metin "KobiFabrika"
    /// okunur. Açılışta App:FrontendUrl'den ayarlanır.
    /// </summary>
    public static string? LogoUrl { get; set; }

    private const string Ink = "#000000";
    private const string Navy = "#000a1d";
    private const string Muted = "#525252";
    private const string Line = "#e5e5e5";
    private const string Accent = "#00b0ff";

    public static EmailMessage ConfirmEmail(string to, string fullName, string link) => Build(
        to,
        "E-posta adresinizi doğrulayın",
        "E-posta adresinizi doğrulayın",
        $"Merhaba {fullName},",
        "KobiFabrika hesabınızı oluşturduğunuz için teşekkürler. E-posta adresinizi doğrulamak için aşağıdaki düğmeye dokunun.",
        "E-postamı doğrula",
        link,
        "Bağlantı 24 saat geçerlidir. Bu hesabı siz oluşturmadıysanız bu e-postayı yok sayabilirsiniz.");

    public static EmailMessage ResetPassword(string to, string fullName, string link) => Build(
        to,
        "Şifrenizi sıfırlayın",
        "Şifrenizi sıfırlayın",
        $"Merhaba {fullName},",
        "Şifrenizi sıfırlamak için bir istek aldık. Yeni şifre belirlemek için aşağıdaki düğmeye dokunun.",
        "Yeni şifre belirle",
        link,
        "Bağlantı 2 saat geçerlidir. Bu isteği siz yapmadıysanız şifreniz değişmez; bu e-postayı yok sayabilirsiniz.");

    public static EmailMessage Invitation(string to, string businessName, string invitedBy, string roleName, string link) => Build(
        to,
        $"{businessName} sizi KobiFabrika'ya davet etti",
        "Ekibe davet edildiniz",
        "Merhaba,",
        $"{invitedBy}, sizi {businessName} işletmesine {roleName} olarak davet etti. Daveti kabul etmek için aşağıdaki düğmeye dokunun.",
        "Daveti kabul et",
        link,
        "Davet 7 gün geçerlidir ve tek kullanımlıktır.");

    public static EmailMessage PasswordChanged(string to, string fullName) => Build(
        to,
        "Şifreniz değiştirildi",
        "Şifreniz değiştirildi",
        $"Merhaba {fullName},",
        "KobiFabrika hesabınızın şifresi az önce değiştirildi. Diğer cihazlardaki oturumlarınız kapatıldı.",
        null,
        null,
        "Bu değişikliği siz yapmadıysanız hemen \"Şifremi unuttum\" ile yeni şifre belirleyin ve bize yazın.");

    /// <summary>Deneme bitiyor / bitti (işletme sahibine).</summary>
    public static EmailMessage TrialNotice(string to, string fullName, string businessName, bool ended, DateTime endsAtUtc, string link) => Build(
        to,
        ended ? $"{businessName}: deneme süresi bitti" : $"{businessName}: deneme süresi {LongDate(endsAtUtc)} tarihinde bitiyor",
        ended ? "Deneme süreniz bitti" : "Deneme süreniz bitmek üzere",
        $"Merhaba {fullName},",
        ended
            ? "14 günlük deneme süreniz bitti; hesabınız ücretsiz Çırak paketiyle devam ediyor. Verileriniz silinmedi. Kampanya, otomasyon ve online randevu gibi özellikler Kalfa ve Usta paketlerinde açılır."
            : $"14 günlük deneme süreniz {LongDate(endsAtUtc)} tarihinde bitiyor. Sonra hesabınız ücretsiz Çırak paketiyle devam eder; verileriniz silinmez. Paketinizi seçmek için aşağıdaki düğmeye dokunun.",
        "Paketleri gör",
        link,
        "Online ödeme yakında. Paket değişikliği için KobiFabrika destekle WhatsApp'tan veya e-postayla iletişime geçebilirsiniz.");

    /// <summary>İşletmenin müşterisine giden ekstre linki (altta işletmenin telefonu ve adresi, varsa).</summary>
    public static EmailMessage Statement(string to, EmailSender sender, string contactName, string range, string link) => Build(
        to,
        $"{sender.Name} cari hesap ekstreniz",
        "Cari hesap ekstreniz hazır",
        $"Merhaba {contactName},",
        $"{sender.Name} işletmesindeki cari hesabınızın {range} dönemi ekstresi hazır. Görmek ve PDF olarak indirmek için aşağıdaki düğmeye dokunun.",
        "Ekstreyi gör",
        link,
        "Bağlantı 30 gün geçerlidir. Sorunuz varsa bu e-postayı yanıtlayarak işletmeye yazabilirsiniz.",
        sender);

    /// <summary>İşletmenin müşterisine giden tahsilat linki (IBAN ve "Ödedim"; altta işletmenin telefonu ve adresi).</summary>
    public static EmailMessage PaymentRequest(string to, EmailSender sender, string contactName, string amount, string link) => Build(
        to,
        $"{sender.Name} ödeme bilgileri",
        "Ödeme bilgileri",
        $"Merhaba {contactName},",
        $"{sender.Name} işletmesine {amount} ödemeniz için banka bilgileri aşağıdaki bağlantıda. Ödemeyi yaptıktan sonra sayfadaki \"Ödedim\" düğmesine dokunun.",
        "Ödeme bilgilerini gör",
        link,
        "Bağlantı 30 gün geçerlidir. Sorunuz varsa bu e-postayı yanıtlayarak işletmeye yazabilirsiniz.",
        sender);

    /// <summary>Vadesi gelen alacak hatırlatması (bilgilendirme; izin aranmaz).</summary>
    public static EmailMessage DueReminder(string to, EmailSender sender, string contactName, string amount, string? link) => Build(
        to,
        $"{sender.Name}: ödeme hatırlatması",
        "Ödeme hatırlatması",
        $"Merhaba {contactName},",
        $"{sender.Name} işletmesine {amount} tutarında vadesi gelen ödemeniz bulunuyor." + (link is null ? " Ödemeniz için teşekkür ederiz." : " Banka bilgileri aşağıdaki bağlantıda."),
        link is null ? null : "Ödeme bilgilerini gör",
        link,
        "Ödemeyi yaptıysanız bu e-postayı dikkate almayın. Sorunuz varsa bu e-postayı yanıtlayarak işletmeye yazabilirsiniz.",
        sender);

    /// <summary>Teklif linki (altta işletmenin telefonu ve adresi, varsa).</summary>
    public static EmailMessage Quote(string to, EmailSender sender, string contactName, string quoteNo, string total, string validUntil, string link) => Build(
        to,
        $"{sender.Name} teklifi: {quoteNo}",
        $"Teklif {quoteNo}",
        $"Merhaba {contactName},",
        $"{sender.Name} size {total} tutarında bir teklif gönderdi. Teklifi aşağıdaki bağlantıdan inceleyip onaylayabilir veya reddedebilirsiniz.",
        "Teklifi gör",
        link,
        $"Teklif {validUntil} tarihine kadar geçerlidir. Sorunuz varsa bu e-postayı yanıtlayarak işletmeye yazabilirsiniz.",
        sender);

    // --- Randevu (müşteriye; bilgilendirme) ---

    public enum AppointmentEmailKind
    {
        Requested,
        Booked,
        Confirmed,
        Reminder,
        Moved,
        Cancelled,
        Rejected,
    }

    /// <summary>
    /// Randevu e-postası. manageLink: müşterinin randevu sayfası (iptal); bookingLink: ret ve iptalde "Yeni randevu al" (online
    /// randevu açıksa).
    /// </summary>
    public static EmailMessage Appointment(
        AppointmentEmailKind kind, string to, EmailSender sender, string contactName, DateTime startUtc, string service, string? staff,
        string? manageLink, string? bookingLink, DateTime nowUtc)
    {
        var day = LongDate(startUtc);
        var time = BusinessTime.ToLocal(startUtc).ToString("HH:mm", Tr);
        var when = RelativeDay(startUtc, nowUtc) is { } rel ? $"{rel} {time}" : $"{day} {time}";
        var details = new List<EmailDetail> { new("Tarih", day), new("Saat", time), new("Hizmet", service) };
        if (!string.IsNullOrWhiteSpace(staff))
        {
            details.Add(new("Personel", staff));
        }

        if (!string.IsNullOrWhiteSpace(sender.Address))
        {
            details.Add(new("Adres", sender.Address));
        }

        var callUs = sender.Phone is { } phone ? $" Değişiklik için işletmeyi arayın: {phone}." : "";
        // Zaten "işletmeyle iletişime geçin" diyen dipnotlarda telefon cümlenin içine girer (tekrar etmez).
        string Contact(string lead) => sender.Phone is { } p ? $"{lead} işletmeyi arayın: {p}." : $"{lead} işletmeyle iletişime geçin.";
        var (subject, title, body, button, link, footnote) = kind switch
        {
            AppointmentEmailKind.Requested => (
                $"{sender.Name}: randevu talebiniz alındı",
                "Randevu talebiniz alındı",
                $"Talebiniz {sender.Name} işletmesine iletildi. İşletme onayladığında size tekrar e-posta göndereceğiz.",
                "Randevumu gör", manageLink,
                "Randevunuzu bu bağlantıdan görebilir, gerekirse iptal edebilirsiniz."),
            AppointmentEmailKind.Booked or AppointmentEmailKind.Confirmed => (
                $"{sender.Name}: randevunuz onaylandı ({when})",
                "Randevunuz onaylandı",
                $"{sender.Name} randevunuzu onayladı. Sizi bekliyoruz.",
                "Randevumu gör", manageLink,
                "Gelemeyecekseniz randevunuzu bu bağlantıdan iptal edebilirsiniz." + callUs),
            AppointmentEmailKind.Reminder => (
                $"{sender.Name}: randevu hatırlatması ({when})",
                $"Randevunuz {when}",
                $"{sender.Name} olarak {service} randevunuzu hatırlatırız. Ayrıntılar aşağıda.",
                "Randevumu gör", manageLink,
                "Gelemeyecekseniz lütfen randevunuzu iptal edin; saat başka bir müşteriye açılsın." + callUs),
            AppointmentEmailKind.Moved => (
                $"{sender.Name}: randevu saatiniz değişti ({when})",
                "Randevu saatiniz değişti",
                $"{sender.Name} randevunuzu yeni saate aldı. Güncel bilgiler aşağıda.",
                "Randevumu gör", manageLink,
                Contact("Yeni saat size uymuyorsa")),
            AppointmentEmailKind.Cancelled => (
                $"{sender.Name}: randevunuz iptal edildi",
                "Randevunuz iptal edildi",
                $"{day} {time} tarihli randevunuz iptal edildi.",
                bookingLink is null ? null : "Yeni randevu al", bookingLink,
                Contact("Bir yanlışlık olduğunu düşünüyorsanız")),
            _ => (
                $"{sender.Name}: randevu talebiniz onaylanamadı",
                "Randevu talebiniz onaylanamadı",
                $"{sender.Name}, {day} {time} için randevu talebinizi onaylayamadı.",
                bookingLink is null ? null : "Başka saat seç", bookingLink,
                sender.Phone is { } tel
                    ? $"Başka bir saat için tekrar randevu alabilir veya işletmeyi arayabilirsiniz: {tel}."
                    : "Başka bir saat için tekrar randevu alabilir veya işletmeyle iletişime geçebilirsiniz."),
        };

        return Build(to, subject, title, $"Merhaba {contactName},", body, button, link, footnote, sender, details);
    }

    /// <summary>"Yarın" / "Bugün" (İstanbul günü), değilse null.</summary>
    private static string? RelativeDay(DateTime startUtc, DateTime nowUtc)
    {
        var diff = BusinessTime.LocalDay(startUtc).DayNumber - BusinessTime.LocalDay(nowUtc).DayNumber;
        return diff switch { 0 => "Bugün", 1 => "Yarın", _ => null };
    }

    /// <summary>"Cumartesi, 18 Ekim 2026".</summary>
    public static string LongDate(DateTime utc)
    {
        var local = BusinessTime.ToLocal(utc);
        var text = local.ToString("dddd, d MMMM yyyy", Tr);
        return char.ToUpper(text[0], Tr) + text[1..];
    }

    /// <param name="sender">Doluysa e-posta işletme adına gider: üstte işletme adı (ve logo), altta iletişim ve "KobiFabrika ile gönderildi".</param>
    /// <param name="unsubscribeLink">Pazarlama e-postasında altta çıkış linki.</param>
    public static EmailMessage Build(
        string to, string subject, string title, string greeting, string body, string? buttonText, string? link, string footnote,
        EmailSender? sender = null, IReadOnlyList<EmailDetail>? details = null, string? unsubscribeLink = null)
    {
        static string E(string s) => Html.Encode(s);

        var header = sender is null
            ? LogoUrl is { } logoUrl
                ? $"""<p style="margin:0 0 20px"><img src="{E(logoUrl)}" alt="KobiFabrika" width="200" height="37" style="display:block;width:200px;height:auto;border:0;border-radius:4px;background:#000000;color:#ffffff;font-size:18px;font-weight:800"></p>"""
                : $"""<p style="margin:0 0 20px;font-size:22px;font-weight:800;letter-spacing:-0.3px;color:{Ink}">Kobi<span style="border-bottom:3px solid {Accent}">Fabrika</span></p>"""
            : $"""
              <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 20px"><tr>
                {(sender.LogoUrl is { } logo ? $"""<td style="padding-right:12px;vertical-align:middle"><img src="{E(logo)}" alt="" width="40" height="40" style="display:block;width:40px;height:40px;border-radius:6px;object-fit:contain"></td>""" : "")}
                <td style="vertical-align:middle;font-size:20px;font-weight:800;color:{Ink}">{E(sender.Name)}</td>
              </tr></table>
              """;

        var table = details is { Count: > 0 }
            ? "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" width=\"100%\" style=\"margin:4px 0 20px;border-top:1px solid " + Line + "\">"
              + string.Concat(details.Select(d =>
                  $"""<tr><td style="padding:10px 12px 10px 0;border-bottom:1px solid {Line};font-size:14px;color:{Muted};width:30%;vertical-align:top">{E(d.Label)}</td><td style="padding:10px 0;border-bottom:1px solid {Line};font-size:16px;color:{Ink};font-weight:600;vertical-align:top">{E(d.Value)}</td></tr>"""))
              + "</table>"
            : "";

        var button = buttonText is null || link is null
            ? ""
            : $"""
              <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 20px"><tr><td style="background:{Navy};border-radius:6px">
                <a href="{E(link)}" style="display:inline-block;padding:14px 22px;font-size:16px;font-weight:700;color:#ffffff;text-decoration:none">{E(buttonText)}</a>
              </td></tr></table>
              <p style="margin:0 0 16px;font-size:13px;color:{Muted};line-height:1.5">Düğme çalışmazsa bu bağlantıyı tarayıcınıza yapıştırın:<br><a href="{E(link)}" style="color:#006ea6;word-break:break-all">{E(link)}</a></p>
              """;

        var contact = sender is null
            ? ""
            : string.Join(" · ", new[] { sender.Phone, sender.Address }.Where(x => !string.IsNullOrWhiteSpace(x))!);
        var footer = new StringBuilder();
        if (sender is not null)
        {
            if (contact.Length > 0)
            {
                footer.Append($"""<p style="margin:0 0 6px;font-size:13px;color:{Muted}">{E(sender.Name)} · {E(contact)}</p>""");
            }

            footer.Append($"""<p style="margin:0;font-size:12px;color:{Muted}">KobiFabrika ile gönderildi.</p>""");
        }
        else
        {
            footer.Append($"""<p style="margin:0;font-size:12px;color:{Muted}">{E(CompanyLine)}</p>""");
        }

        if (unsubscribeLink is not null)
        {
            footer.Append($"""<p style="margin:8px 0 0;font-size:12px;color:{Muted}">Bu e-postaları almak istemiyorsanız: <a href="{E(unsubscribeLink)}" style="color:{Muted}">e-postaları durdur</a></p>""");
        }

        var html = $"""
            <!doctype html>
            <html lang="tr">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>{E(subject)}</title></head>
            <body style="margin:0;padding:0;background:#f4f4f4;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:{Ink};-webkit-text-size-adjust:100%">
              <div style="max-width:560px;margin:0 auto;padding:24px 16px">
                {header}
                <div style="background:#ffffff;border:1px solid {Line};border-radius:6px;padding:24px 20px">
                  <h1 style="margin:0 0 16px;font-size:22px;line-height:1.3;font-weight:800;color:{Ink}">{E(title)}</h1>
                  <p style="margin:0 0 8px;font-size:16px;line-height:1.5">{E(greeting)}</p>
                  <p style="margin:0 0 16px;font-size:16px;line-height:1.5">{E(body)}</p>
                  {table}
                  {button}
                  <p style="margin:0;font-size:14px;color:{Muted};line-height:1.5">{E(footnote)}</p>
                </div>
                <div style="padding:16px 4px 0">{footer}</div>
              </div>
            </body>
            </html>
            """;

        var text = new StringBuilder();
        text.Append(sender is null ? "KobiFabrika" : sender.Name).Append("\n\n").Append(title).Append("\n\n")
            .Append(greeting).Append("\n\n").Append(body).Append("\n\n");
        foreach (var d in details ?? [])
        {
            text.Append(d.Label).Append(": ").Append(d.Value).Append('\n');
        }

        if (details is { Count: > 0 })
        {
            text.Append('\n');
        }

        if (buttonText is not null && link is not null)
        {
            text.Append(buttonText).Append(": ").Append(link).Append("\n\n");
        }

        text.Append(footnote).Append("\n\n");
        text.Append(sender is null ? CompanyLine : (contact.Length > 0 ? $"{sender.Name} · {contact}\n" : "") + "KobiFabrika ile gönderildi.");
        if (unsubscribeLink is not null)
        {
            text.Append("\n\nE-postaları durdurmak için: ").Append(unsubscribeLink);
        }

        return new EmailMessage(to, subject, html, text.ToString(), ListUnsubscribe: unsubscribeLink, SenderBusinessName: sender?.Name);
    }
}
