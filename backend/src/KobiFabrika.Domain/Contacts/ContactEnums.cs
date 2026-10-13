namespace KobiFabrika.Domain.Contacts;

/// <summary>Müşterinin bizi nereden bulduğu. Liste sabit (arayüzde Türkçe etiketlerle).</summary>
public enum ContactSource
{
    Instagram,
    Google,
    Referral,
    WalkIn,
    Facebook,
    TikTok,
    Other,

    /// <summary>Online randevu sayfasından gelen yeni müşteri (sistem yazar).</summary>
    OnlineBooking,
}

/// <summary>KVKK/İYS izin kanalı.</summary>
public enum ConsentChannel
{
    Sms,
    Email,
    Call,
}

/// <summary>İznin nereden geldiği.</summary>
public enum ConsentSource
{
    Panel,
    Import,
    OnlineBooking,
}

/// <summary>Etiket rengi; sadece 3 renk kuralına uyan tonlar (docs/04-tasarim.md).</summary>
public enum TagColor
{
    Gray,
    Black,
    Blue,
    Outline,
}
