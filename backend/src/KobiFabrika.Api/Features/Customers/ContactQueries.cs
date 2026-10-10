using System.Text.Json;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>
/// Müşteri listesi filtresi; kayıtlı segmentin tanımı da bu biçimde (JSON) saklanır.
/// Type: "customer" | "supplier" | null (hepsi). Balance: "debtors" (bize borçlu) | "creditors" (biz borçluyuz) | "nonzero".
/// TagIds: seçilen etiketlerden herhangi birine sahip olanlar. InactiveDays: son ziyareti bu kadar günden eski olanlar.
/// </summary>
public sealed record ContactFilter(
    string? Type = null,
    IReadOnlyList<Guid>? TagIds = null,
    ContactSource? Source = null,
    string? Balance = null,
    int? InactiveDays = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static readonly string[] BalanceValues = ["debtors", "creditors", "nonzero"];
    public static readonly string[] TypeValues = ["customer", "supplier"];

    public bool UsesBalance => Balance is not null;

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static ContactFilter Deserialize(string json) => JsonSerializer.Deserialize<ContactFilter>(json, Json) ?? new ContactFilter();
}

public static class ContactQueries
{
    public static bool IsSystemSegment(string code) => SystemSegments.All.Any(s => s.Code == code);

    /// <summary>Hazır segment Bakiye bilgisine dayanıyor mu (Personel göremez).</summary>
    public static bool SystemSegmentUsesBalance(string code) => code == SystemSegments.Debtors;

    public static IQueryable<Contact> Apply(this IQueryable<Contact> query, ContactFilter filter, AppDbContext db, DateTime nowUtc)
    {
        query = filter.Type switch
        {
            "customer" => query.Where(c => c.IsCustomer),
            "supplier" => query.Where(c => c.IsSupplier),
            _ => query,
        };

        if (filter.TagIds is { Count: > 0 } tagIds)
        {
            query = query.Where(c => db.ContactTags.Any(t => t.ContactId == c.Id && tagIds.Contains(t.TagId)));
        }

        if (filter.Source is { } source)
        {
            query = query.Where(c => c.Source == source);
        }

        query = filter.Balance switch
        {
            "debtors" => query.Where(c => c.Balance > 0),
            "creditors" => query.Where(c => c.Balance < 0),
            "nonzero" => query.Where(c => c.Balance != 0),
            _ => query,
        };

        if (filter.InactiveDays is { } days and > 0)
        {
            var before = nowUtc.AddDays(-days);
            query = query.Where(c => c.LastVisitAt != null && c.LastVisitAt < before);
        }

        return query;
    }

    public static IQueryable<Contact> ApplySystemSegment(this IQueryable<Contact> query, string code, TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        switch (code)
        {
            case SystemSegments.Inactive60:
                var before = now.AddDays(-60);
                return query.Where(c => c.LastVisitAt != null && c.LastVisitAt < before);
            case SystemSegments.Debtors:
                return query.Where(c => c.Balance > 0);
            case SystemSegments.BirthdayThisMonth:
                var month = BusinessTime.Today(clock).Month;
                return query.Where(c => c.BirthDate != null && c.BirthDate.Value.Month == month);
            case SystemSegments.New30:
                var since = now.AddDays(-30);
                return query.Where(c => c.CreatedAt >= since);
            default:
                return query;
        }
    }

    /// <summary>
    /// Serbest metin araması: ad/firma, e-posta ve telefon. Telefonda girdideki rakamlar kullanılır
    /// ("0532 123", "532-123", "+90 532" aynı sonucu verir).
    /// </summary>
    public static IQueryable<Contact> Search(this IQueryable<Contact> query, string? text)
    {
        var term = text?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return query;
        }

        var digits = PhoneDigits(term);
        return digits is not null
            ? query.Where(c => c.DisplayName.Contains(term) || (c.Phone != null && c.Phone.Contains(digits)))
            : query.Where(c => c.DisplayName.Contains(term) || (c.CompanyName != null && c.CompanyName.Contains(term))
                || (c.Email != null && c.Email.Contains(term)));
    }

    /// <summary>Arama metni telefon gibi görünüyorsa (en az 3 rakam, harf yok) ulusal numara parçası; değilse null.</summary>
    public static string? PhoneDigits(string term)
    {
        if (term.Any(char.IsLetter) || term.Contains('@'))
        {
            return null;
        }

        var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 3)
        {
            return null;
        }

        if (term.TrimStart().StartsWith('+') && digits.StartsWith("90", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.StartsWith("90", StringComparison.Ordinal) && digits.Length >= 12)
        {
            digits = digits[2..];
        }
        else if (digits.StartsWith('0'))
        {
            digits = digits.TrimStart('0');
        }

        return digits.Length >= 3 ? digits : null;
    }

    public static IQueryable<Contact> Sort(this IQueryable<Contact> query, string? sort) => sort switch
    {
        "recent" => query.OrderByDescending(c => c.CreatedAt),
        "balance" => query.OrderByDescending(c => c.Balance).ThenBy(c => c.DisplayName),
        "lastVisit" => query.OrderByDescending(c => c.LastVisitAt).ThenBy(c => c.DisplayName),
        _ => query.OrderBy(c => c.DisplayName).ThenBy(c => c.Id),
    };

    public static async Task<Dictionary<Guid, List<TagDto>>> TagsForAsync(AppDbContext db, IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        if (contactIds.Count == 0)
        {
            return [];
        }

        var rows = await db.ContactTags
            .Where(ct2 => contactIds.Contains(ct2.ContactId))
            .Join(db.Tags, ct2 => ct2.TagId, t => t.Id, (ct2, t) => new { ct2.ContactId, t.Id, t.Name, t.Color })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ContactId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Name).Select(r => new TagDto(r.Id, r.Name, r.Color)).ToList());
    }
}
