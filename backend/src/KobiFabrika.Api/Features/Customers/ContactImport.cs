using FluentValidation;
using KobiFabrika.Api.Features.Imports;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Locations;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>Bir satırın içe aktarmaya hazır, doğrulanmış hali.</summary>
public sealed record ImportCandidate(
    int RowNumber,
    string? FirstName,
    string? LastName,
    string? CompanyName,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    string? Address,
    string? City,
    string? District,
    ContactSource? Source,
    IReadOnlyList<string> Tags,
    bool Sms,
    bool EmailConsent,
    bool Call,
    string? Notes,
    decimal? OpeningBalance);

/// <summary>
/// Excel içe aktarma kuralları: şablon, başlık eşleme önerisi, hücre çözümleme ve satır doğrulama.
/// Telefon zorunlu değil; girilmişse geçerli olmalı. Aynı telefon veritabanında veya dosyada daha önce varsa satır atlanır.
/// </summary>
public static class ContactImport
{
    public static readonly IReadOnlyList<ImportFieldInfo> Fields =
    [
        new("firstName", "Ad"),
        new("lastName", "Soyad"),
        new("companyName", "Firma"),
        new("phone", "Telefon"),
        new("email", "E-posta"),
        new("birthDate", "Doğum tarihi"),
        new("address", "Adres"),
        new("city", "İl"),
        new("district", "İlçe"),
        new("source", "Kaynak"),
        new("tags", "Etiketler"),
        new("smsConsent", "SMS izni"),
        new("emailConsent", "E-posta izni"),
        new("callConsent", "Arama izni"),
        new("notes", "Not"),
        new("openingBalance", "Açılış bakiyesi"),
    ];

    /// <summary>Başlık eş anlamlıları (küçük harf, Türkçe karaktersiz).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>
    {
        ["firstName"] = ["ad", "adi", "isim", "musteri adi", "ad soyad", "adi soyadi"],
        ["lastName"] = ["soyad", "soyadi"],
        ["companyName"] = ["firma", "firma adi", "sirket", "sirket adi", "unvan"],
        ["phone"] = ["telefon", "tel", "cep", "cep telefonu", "gsm", "telefon numarasi", "cep tel"],
        ["email"] = ["e-posta", "eposta", "email", "e-mail", "mail"],
        ["birthDate"] = ["dogum tarihi", "dogum gunu", "dogum"],
        ["address"] = ["adres"],
        ["city"] = ["il", "sehir"],
        ["district"] = ["ilce"],
        ["source"] = ["kaynak", "nereden"],
        ["tags"] = ["etiketler", "etiket", "grup"],
        ["smsConsent"] = ["sms izni", "sms"],
        ["emailConsent"] = ["e-posta izni", "eposta izni", "email izni", "mail izni"],
        ["callConsent"] = ["arama izni", "arama"],
        ["notes"] = ["not", "notlar", "aciklama"],
        ["openingBalance"] = ["acilis bakiyesi", "bakiye", "borc", "alacak"],
    };

    private static string Simplify(string text) => ImportJobs.Simplify(text);

    /// <summary>Başlıklara göre alan → sütun önerisi (bulunamayan alan eşlenmez).</summary>
    public static Dictionary<string, int> SuggestMapping(IReadOnlyList<string> headers) => ImportJobs.SuggestMapping(headers, Fields, Aliases);

    public static byte[] Template() => ImportJobs.Template("Müşteriler", Fields, [4],
    [
        ["Alan", "Nasıl doldurulur", "Örnek"],
        ["Ad / Soyad / Firma", "Ad veya firma adından en az biri zorunlu.", "Ayşe / Yılmaz"],
        ["Telefon", "İsteğe bağlı. Aynı numara zaten kayıtlıysa satır atlanır.", "0532 123 45 67"],
        ["E-posta", "İsteğe bağlı.", "ayse@ornek.com"],
        ["Doğum tarihi", "GG.AA.YYYY", "14.05.1990"],
        ["İl / İlçe", "Türkiye il ve ilçe adları.", "İstanbul / Kadıköy"],
        ["Kaynak", string.Join(", ", ContactLabels.Sources.Values), "Instagram"],
        ["Etiketler", "Virgülle ayırın; olmayan etiket oluşturulur.", "VIP, Düzenli"],
        ["SMS / E-posta / Arama izni", "Evet veya Hayır. Boş = Hayır. Sadece müşteriden izin aldıysanız Evet yazın.", "Evet"],
        ["Açılış bakiyesi", "Müşteri size borçluysa artı, siz borçluysanız eksi tutar. Sadece Sahip ve Yönetici aktarabilir.", "1.250,50"],
        ["", "En fazla 5000 satır. Bu sayfa içe aktarılmaz.", ""],
        
    ]);

    /// <summary>Satırı doğrular. Hata varsa <c>Candidate</c> null.</summary>
    public static (ImportCandidate? Candidate, List<string> Errors, List<string> Warnings) Parse(
        int rowNumber, string?[] cells, IReadOnlyDictionary<string, int> mapping, LocationCatalog locations, bool canSetOpeningBalance)
    {
        string? Get(string field) =>
            mapping.TryGetValue(field, out var i) && i >= 0 && i < cells.Length && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i]!.Trim() : null;

        var errors = new List<string>();
        var warnings = new List<string>();

        var first = Get("firstName");
        var last = Get("lastName");
        var company = Get("companyName");
        if (first is null && last is null && company is null)
        {
            errors.Add("Ad veya firma adı yok.");
        }

        if ((first?.Length ?? 0) > 100 || (last?.Length ?? 0) > 100 || (company?.Length ?? 0) > 200)
        {
            errors.Add("Ad en fazla 100, firma adı en fazla 200 karakter olabilir.");
        }

        string? phone = null;
        if (Get("phone") is { } rawPhone)
        {
            if (PhoneNumber.TryNormalize(rawPhone, out var e164))
            {
                phone = e164;
            }
            else
            {
                errors.Add($"Telefon geçersiz: {rawPhone}");
            }
        }

        var email = Get("email");
        if (email is not null)
        {
            email = email.ToLowerInvariant();
            if (email.Length > 256 || !EmailValidator.IsValid(email))
            {
                errors.Add($"E-posta geçersiz: {email}");
            }
        }

        DateOnly? birthDate = null;
        if (Get("birthDate") is { } rawDate)
        {
            if (ParseDate(rawDate) is { } d && d.Year >= 1900 && d <= DateOnly.FromDateTime(DateTime.UtcNow))
            {
                birthDate = d;
            }
            else
            {
                errors.Add($"Doğum tarihi anlaşılamadı: {rawDate} (GG.AA.YYYY yazın)");
            }
        }

        var address = Get("address");
        if (address?.Length > 500)
        {
            errors.Add("Adres en fazla 500 karakter olabilir.");
        }

        string? city = null, district = null;
        if (Get("city") is { } rawCity)
        {
            var match = locations.Cities.FirstOrDefault(c => Simplify(c.Name) == Simplify(rawCity));
            if (match is null)
            {
                errors.Add($"İl bulunamadı: {rawCity}");
            }
            else
            {
                city = match.Name;
                if (Get("district") is { } rawDistrict)
                {
                    district = match.Districts.FirstOrDefault(d => Simplify(d) == Simplify(rawDistrict));
                    if (district is null)
                    {
                        errors.Add($"İlçe bulunamadı: {rawDistrict}");
                    }
                }
            }
        }

        ContactSource? source = null;
        if (Get("source") is { } rawSource)
        {
            source = ContactLabels.Sources
                .Where(s => Simplify(s.Value) == Simplify(rawSource) || Simplify(s.Key.ToString()) == Simplify(rawSource))
                .Select(s => (ContactSource?)s.Key)
                .FirstOrDefault();
            if (source is null)
            {
                errors.Add($"Kaynak listede yok: {rawSource} ({string.Join(", ", ContactLabels.Sources.Values)})");
            }
        }

        var tags = (Get("tags") ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
        if (tags.Any(t => t.Length > 50))
        {
            errors.Add("Etiket adı en fazla 50 karakter olabilir.");
        }

        bool Consent(string field, string label)
        {
            if (Get(field) is not { } raw)
            {
                return false;
            }

            if (ImportJobs.ParseYesNo(raw) is { } value)
            {
                return value;
            }

            errors.Add($"{label} için Evet veya Hayır yazın: {raw}");
            return false;
        }

        var sms = Consent("smsConsent", "SMS izni");
        var emailConsent = Consent("emailConsent", "E-posta izni");
        var call = Consent("callConsent", "Arama izni");

        var notes = Get("notes");
        if (notes?.Length > 2000)
        {
            errors.Add("Not en fazla 2000 karakter olabilir.");
        }

        decimal? opening = null;
        if (Get("openingBalance") is { } rawBalance)
        {
            if (ImportJobs.ParseMoney(rawBalance) is not { } amount)
            {
                errors.Add($"Açılış bakiyesi anlaşılamadı: {rawBalance}");
            }
            else if (amount != 0 && Money.Validate(Math.Abs(amount)) is { } moneyError)
            {
                errors.Add($"Açılış bakiyesi: {moneyError}");
            }
            else if (amount != 0 && !canSetOpeningBalance)
            {
                warnings.Add("Açılış bakiyesi aktarılmadı (sadece Sahip ve Yönetici).");
            }
            else if (amount != 0)
            {
                opening = amount;
            }
        }

        if (errors.Count > 0)
        {
            return (null, errors, warnings);
        }

        return (new ImportCandidate(rowNumber, first, last, company, phone, email, birthDate, address, city, district, source, tags,
            sms, emailConsent, call, notes, opening), errors, warnings);
    }

    public static string DisplayName(ImportCandidate c)
    {
        var contact = new Contact { FirstName = c.FirstName, LastName = c.LastName, CompanyName = c.CompanyName };
        contact.RefreshDisplayName();
        return contact.DisplayName;
    }

    public static string? DisplayName(string?[] cells, IReadOnlyDictionary<string, int> mapping)
    {
        string? Get(string field) => mapping.TryGetValue(field, out var i) && i >= 0 && i < cells.Length ? cells[i]?.Trim() : null;
        var contact = new Contact { FirstName = Get("firstName"), LastName = Get("lastName"), CompanyName = Get("companyName") };
        contact.RefreshDisplayName();
        return contact.DisplayName.Length > 0 ? contact.DisplayName : null;
    }

    public static DateOnly? ParseDate(string text) => ImportJobs.ParseDate(text);

    private static class EmailValidator
    {
        private static readonly InlineValidator<string> Validator = CreateValidator();

        private static InlineValidator<string> CreateValidator()
        {
            var v = new InlineValidator<string>();
            v.RuleFor(x => x).EmailAddress();
            return v;
        }

        public static bool IsValid(string email) => Validator.Validate(email).IsValid;
    }
}
