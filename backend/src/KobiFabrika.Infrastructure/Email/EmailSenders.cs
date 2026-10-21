using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Messaging;

namespace KobiFabrika.Infrastructure.Email;

/// <summary>
/// İşletme adına giden e-postaların gönderen bilgisi tek yerden: ad, logo, telefon ve adres (varsa). Ekstre, tahsilat linki, teklif
/// ve vade hatırlatması altında işletmenin telefonu ve adresi görünür (2026-10-21); randevu e-postaları da aynı biçimi kullanır.
/// </summary>
public static class EmailSenders
{
    public static EmailSender For(Business business, AppOptions app) => For(
        business.Name, app.ApiLink(business.LogoUrl), business.Phone, business.Address, business.District, business.City);

    public static EmailSender For(string name, string? logoUrl, string? phone, string? address, string? district, string? city) =>
        new(name, logoUrl, phone is { Length: > 0 } p ? AppointmentNotifier.FormatPhone(p) : null, AddressOf(address, district, city));

    /// <summary>"Atatürk Cd. 12, Kadıköy, İstanbul"; hiçbiri yoksa null.</summary>
    public static string? AddressOf(string? address, string? district, string? city) =>
        string.Join(", ", new[] { address, district, city }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } a ? a : null;
}
