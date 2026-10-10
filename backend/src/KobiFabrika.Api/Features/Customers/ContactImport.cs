using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FluentValidation;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Locations;

namespace KobiFabrika.Api.Features.Customers;

public enum ImportRowStatus
{
    Ok,
    Error,
    Duplicate,
}

public sealed record ImportFieldInfo(string Key, string Label);

/// <summary>Satır sonucu. Messages: hata veya atlanma nedeni, ya da (Ok için) uyarı.</summary>
public sealed record ImportRowResult(int RowNumber, string? Name, string? Phone, ImportRowStatus Status, IReadOnlyList<string> Messages);

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
    public const long MaxFileBytes = 5 * 1024 * 1024;

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
    private static readonly Dictionary<string, string[]> Aliases = new()
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

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Simplify(string text)
    {
        var lower = text.Trim().ToLower(Tr)
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var sb = new StringBuilder();
        foreach (var ch in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return string.Join(' ', sb.ToString().Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Başlıklara göre alan → sütun önerisi (bulunamayan alan eşlenmez).</summary>
    public static Dictionary<string, int> SuggestMapping(IReadOnlyList<string> headers)
    {
        var mapping = new Dictionary<string, int>();
        var simplified = headers.Select(Simplify).ToList();
        foreach (var field in Fields)
        {
            var label = Simplify(field.Label);
            var index = simplified.FindIndex(h => h == label || Aliases[field.Key].Contains(h));
            if (index >= 0 && !mapping.ContainsValue(index))
            {
                mapping[field.Key] = index;
            }
        }

        return mapping;
    }

    public static byte[] Template()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Müşteriler");
        for (var i = 0; i < Fields.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = Fields[i].Label;
            cell.Style.Font.Bold = true;
        }

        sheet.Column(4).Style.NumberFormat.Format = "@";
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        var help = book.AddWorksheet("Açıklama");
        string[][] lines =
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
        ];
        for (var r = 0; r < lines.Length; r++)
        {
            for (var c = 0; c < lines[r].Length; c++)
            {
                help.Cell(r + 1, c + 1).Value = lines[r][c];
            }
        }

        help.Row(1).Style.Font.Bold = true;
        help.Columns().AdjustToContents();
        return Finance.LedgerEndpoints.Save(book);
    }

    /// <summary>İlk sayfanın ilk satırı başlık; sonraki boş olmayan satırlar veri. Hücreler metne çevrilir (tarih → yyyy-MM-dd).</summary>
    public static (List<string> Headers, List<(int RowNumber, string?[] Cells)> Rows) Read(Stream stream)
    {
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.First();
        var range = sheet.RangeUsed();
        if (range is null)
        {
            return ([], []);
        }

        var lastColumn = range.LastColumn().ColumnNumber();
        var headerRow = range.FirstRow().RowNumber();
        var headers = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(headerRow, c).GetFormattedString().Trim()).ToList();

        var rows = new List<(int, string?[])>();
        for (var r = headerRow + 1; r <= range.LastRow().RowNumber(); r++)
        {
            var cells = Enumerable.Range(1, lastColumn).Select(c => CellText(sheet.Cell(r, c))).ToArray();
            if (cells.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                rows.Add((r, cells));
            }
        }

        return (headers, rows);
    }

    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "Evet" : "Hayır",
            _ => cell.GetFormattedString().Trim(),
        };
    }

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

            switch (Simplify(raw))
            {
                case "evet" or "e" or "var" or "1" or "x" or "true" or "izinli" or "onay":
                    return true;
                case "hayir" or "h" or "yok" or "0" or "false" or "izinsiz":
                    return false;
                default:
                    errors.Add($"{label} için Evet veya Hayır yazın: {raw}");
                    return false;
            }
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
            if (ParseMoney(rawBalance) is not { } amount)
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

    private static readonly string[] DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d.M.yy", "dd.MM.yy"];

    public static DateOnly? ParseDate(string text) =>
        DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>"1.250,50", "1250,5", "1250.50", "-300", "1.250,50 TL" (Excel sayısı InvariantCulture metin olarak gelir).</summary>
    public static decimal? ParseMoney(string text)
    {
        var t = text.Replace("TL", "", StringComparison.OrdinalIgnoreCase).Replace("₺", "").Replace(" ", "").Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if (t.Contains(','))
        {
            // Türkçe biçim: nokta binlik, virgül ondalık.
            return decimal.TryParse(t.Replace(".", "").Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var tr) ? tr : null;
        }

        // Tek nokta ve ardından 3 hane binlik ayırıcı sayılır ("1.250"); diğer durumda ondalık.
        var parts = t.Split('.');
        if (parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 3 && t.TrimStart('-').Length > 4))
        {
            t = t.Replace(".", "");
        }

        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

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
