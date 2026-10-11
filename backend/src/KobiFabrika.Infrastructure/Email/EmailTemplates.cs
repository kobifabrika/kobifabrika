using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace KobiFabrika.Infrastructure.Email;

/// <summary>
/// Sistem e-postaları: sade, satır içi stilli HTML + düz metin. Marka renkleri docs/04-tasarim.md'den.
/// </summary>
public static class EmailTemplates
{
    /// <summary>Türkçe karakterleri olduğu gibi bırakır; sadece HTML'e özel karakterleri kodlar.</summary>
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public static EmailMessage ConfirmEmail(string to, string fullName, string link) => Build(
        to,
        "E-posta adresini doğrula",
        $"Merhaba {fullName},",
        "KobiFabrika hesabını oluşturduğun için teşekkürler. E-posta adresini doğrulamak için aşağıdaki butona dokun.",
        "E-postamı doğrula",
        link,
        "Bağlantı 24 saat geçerlidir. Bu hesabı sen oluşturmadıysan bu e-postayı yok sayabilirsin.");

    public static EmailMessage ResetPassword(string to, string fullName, string link) => Build(
        to,
        "Şifreni sıfırla",
        $"Merhaba {fullName},",
        "Şifreni sıfırlamak için bir istek aldık. Yeni şifre belirlemek için aşağıdaki butona dokun.",
        "Yeni şifre belirle",
        link,
        "Bağlantı 2 saat geçerlidir. Bu isteği sen yapmadıysan şifren değişmez; bu e-postayı yok sayabilirsin.");

    public static EmailMessage Invitation(string to, string businessName, string invitedBy, string roleName, string link) => Build(
        to,
        $"{businessName} seni KobiFabrika'ya davet etti",
        "Merhaba,",
        $"{invitedBy}, seni {businessName} işletmesine {roleName} olarak davet etti. Daveti kabul etmek için aşağıdaki butona dokun.",
        "Daveti kabul et",
        link,
        "Davet 7 gün geçerlidir ve tek kullanımlıktır.");

    public static EmailMessage PasswordChanged(string to, string fullName) => Build(
        to,
        "Şifren değiştirildi",
        $"Merhaba {fullName},",
        "KobiFabrika hesabının şifresi az önce değiştirildi. Diğer cihazlardaki oturumların kapatıldı.",
        null,
        null,
        "Bu değişikliği sen yapmadıysan hemen \"Şifremi unuttum\" ile yeni şifre belirle ve bize yaz.");

    /// <summary>İşletmenin müşterisine giden ekstre linki. Üstte işletme adı; KobiFabrika sadece altta küçük.</summary>
    public static EmailMessage Statement(string to, string businessName, string contactName, string range, string link) => Build(
        to,
        $"{businessName} cari hesap ekstreniz",
        $"Merhaba {contactName},",
        $"{businessName} işletmesindeki cari hesabınızın {range} dönemi ekstresi hazır. Görmek ve PDF olarak indirmek için aşağıdaki butona dokunun.",
        "Ekstreyi gör",
        link,
        "Bağlantı 30 gün geçerlidir. Sorunuz varsa doğrudan işletmeyle iletişime geçin.",
        senderName: businessName);

    /// <summary>İşletmenin müşterisine giden tahsilat linki (IBAN ve "Ödedim").</summary>
    public static EmailMessage PaymentRequest(string to, string businessName, string contactName, string amount, string link) => Build(
        to,
        $"{businessName} ödeme bilgileri",
        $"Merhaba {contactName},",
        $"{businessName} işletmesine {amount} ödemeniz için banka bilgileri aşağıdaki bağlantıda. Ödemeyi yaptıktan sonra sayfadaki \"Ödedim\" butonuna dokunun.",
        "Ödeme bilgilerini gör",
        link,
        "Bağlantı 30 gün geçerlidir. Sorunuz varsa doğrudan işletmeyle iletişime geçin.",
        senderName: businessName);

    public static EmailMessage Quote(string to, string businessName, string contactName, string quoteNo, string total, string validUntil, string link) => Build(
        to,
        $"{businessName} teklifi: {quoteNo}",
        $"Merhaba {contactName},",
        $"{businessName} size {total} tutarında bir teklif gönderdi. Teklifi aşağıdaki bağlantıdan inceleyip onaylayabilir veya reddedebilirsiniz.",
        "Teklifi gör",
        link,
        $"Teklif {validUntil} tarihine kadar geçerlidir. Sorunuz varsa doğrudan işletmeyle iletişime geçin.",
        senderName: businessName);

    /// <param name="senderName">Doluysa e-posta işletme adına gider: üstte işletme adı, altta "KobiFabrika ile gönderildi".</param>
    private static EmailMessage Build(
        string to, string subject, string greeting, string body, string? buttonText, string? link, string footnote, string? senderName = null)
    {
        static string E(string s) => Html.Encode(s);

        var button = buttonText is null || link is null
            ? ""
            : $"""
              <p style="margin:24px 0">
                <a href="{E(link)}" style="display:inline-block;background:#000000;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:6px;font-size:16px">{E(buttonText)}</a>
              </p>
              <p style="margin:0 0 16px;font-size:14px;color:#525252">Buton çalışmazsa bu bağlantıyı tarayıcına yapıştır:<br><a href="{E(link)}" style="color:#000000;word-break:break-all">{E(link)}</a></p>
              """;

        var header = senderName is null
            ? """<p style="margin:0 0 16px;font-size:24px;font-weight:800;color:#000000">Kobi<span style="border-bottom:3px solid #00b0ff">Fabrika</span></p>"""
            : $"""<p style="margin:0 0 16px;font-size:22px;font-weight:800;color:#000000">{E(senderName)}</p>""";
        var signature = senderName is null ? "KobiFabrika Yazılım A.Ş." : "KobiFabrika ile gönderildi";

        var html = $"""
            <!doctype html>
            <html lang="tr">
            <body style="margin:0;background:#f2f2f2;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:#000000">
              <div style="max-width:560px;margin:0 auto;padding:24px 16px">
                {header}
                <div style="background:#ffffff;border:1px solid #e5e5e5;border-radius:6px;padding:24px">
                  <p style="margin:0 0 12px;font-size:16px">{E(greeting)}</p>
                  <p style="margin:0 0 12px;font-size:16px;line-height:1.5">{E(body)}</p>
                  {button}
                  <p style="margin:0;font-size:14px;color:#525252;line-height:1.5">{E(footnote)}</p>
                </div>
                <p style="margin:16px 0 0;font-size:12px;color:#525252">{E(signature)}</p>
              </div>
            </body>
            </html>
            """;

        var text = $"{greeting}\n\n{body}\n\n{(link is null ? "" : $"{buttonText}: {link}\n\n")}{footnote}\n\n{signature}";
        return new EmailMessage(to, subject, html, text);
    }
}
