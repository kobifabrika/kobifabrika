using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Infrastructure.Locations;

namespace KobiFabrika.Api.Features.Customers;

public sealed record TagDto(Guid Id, string Name, TagColor Color);

public sealed record ConsentStateDto(ConsentChannel Channel, bool Granted, DateTime? At, ConsentSource? Source);

/// <summary>Bakiyenin yönü: Receivable = müşteri bize borçlu ("Alacak"), Payable = biz borçluyuz ("Borç").</summary>
public enum BalanceDirection
{
    Receivable,
    Payable,
}

public sealed record OpeningBalanceRequest(decimal? Amount, BalanceDirection Direction, DateOnly? Date);

public sealed record ConsentsRequest(bool Sms, bool Email, bool Call);

public sealed record ContactRequest(
    string? FirstName,
    string? LastName,
    string? CompanyName,
    bool IsCustomer,
    bool IsSupplier,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    string? Address,
    string? City,
    string? District,
    ContactSource? Source,
    string? Notes,
    string? TaxNumber,
    string? TaxOffice,
    IReadOnlyList<Guid>? TagIds,
    ConsentsRequest? Consents,
    OpeningBalanceRequest? OpeningBalance);

/// <summary>Liste satırı. <see cref="Balance"/> Personel (Staff) rolünde her zaman null döner.</summary>
public sealed record ContactListItem(
    Guid Id,
    string DisplayName,
    string? CompanyName,
    string? Phone,
    string? Email,
    bool IsCustomer,
    bool IsSupplier,
    ContactSource? Source,
    IReadOnlyList<TagDto> Tags,
    decimal? Balance,
    DateTime? LastVisitAt,
    bool IsArchived,
    DateTime CreatedAt);

public sealed record ContactListResponse(IReadOnlyList<ContactListItem> Items, int Total);

public sealed record ContactResponse(
    Guid Id,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? CompanyName,
    bool IsCustomer,
    bool IsSupplier,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    string? Address,
    string? City,
    string? District,
    ContactSource? Source,
    string? Notes,
    string? TaxNumber,
    string? TaxOffice,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<ConsentStateDto> Consents,
    decimal? Balance,
    bool HasLedgerEntries,
    DateTime? LastVisitAt,
    bool IsArchived,
    DateTime? ArchivedAt,
    DateTime CreatedAt);

public sealed record PhoneCheckResponse(bool Valid, string? Normalized, Guid? ExistingContactId, string? ExistingContactName, bool ExistingIsArchived);

public sealed record NoteResponse(Guid Id, string Body, string? AuthorName, Guid? AuthorId, DateTime CreatedAt);

public sealed record NoteRequest(string Body);

public sealed record SetTagsRequest(IReadOnlyList<Guid> TagIds);

public sealed class ContactRequestValidator : AbstractValidator<ContactRequest>
{
    public ContactRequestValidator(LocationCatalog locations)
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.FirstName) || !string.IsNullOrWhiteSpace(x.LastName) || !string.IsNullOrWhiteSpace(x.CompanyName))
            .WithName("firstName").OverridePropertyName("firstName")
            .WithMessage("Ad veya firma adı girin.");
        RuleFor(x => x.FirstName).MaximumLength(100).WithMessage("Ad en fazla 100 karakter olabilir.");
        RuleFor(x => x.LastName).MaximumLength(100).WithMessage("Soyad en fazla 100 karakter olabilir.");
        RuleFor(x => x.CompanyName).MaximumLength(200).WithMessage("Firma adı en fazla 200 karakter olabilir.");
        RuleFor(x => x).Must(x => x.IsCustomer || x.IsSupplier)
            .OverridePropertyName("isCustomer").WithMessage("Kaydın müşteri mi tedarikçi mi olduğunu seçin.");
        RuleFor(x => x.Phone).ValidPhone().When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Email!).ValidEmail().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.BirthDate)
            .Must(d => d is null || (d.Value.Year >= 1900 && d.Value <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))))
            .WithMessage("Geçerli bir doğum tarihi girin.");
        RuleFor(x => x.Address).MaximumLength(500).WithMessage("Adres en fazla 500 karakter olabilir.");
        RuleFor(x => x.City).Must(c => locations.Cities.Any(l => l.Name == c)).WithMessage("İl seçin.")
            .When(x => !string.IsNullOrEmpty(x.City));
        RuleFor(x => x.District).Must((x, d) => locations.IsValid(x.City, d)).WithMessage("İlçe seçin.")
            .When(x => !string.IsNullOrEmpty(x.District));
        RuleFor(x => x.Source).IsInEnum().WithMessage("Kaynak seçin.");
        RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("Not en fazla 2000 karakter olabilir.");
        RuleFor(x => x.TaxNumber).Matches("^[0-9]{10,11}$").WithMessage("Vergi/TC kimlik no 10 veya 11 rakam olmalı.")
            .When(x => !string.IsNullOrWhiteSpace(x.TaxNumber));
        RuleFor(x => x.TaxOffice).MaximumLength(100).WithMessage("Vergi dairesi en fazla 100 karakter olabilir.");
        RuleFor(x => x.TagIds).Must(t => t is null || t.Count <= 20).WithMessage("En fazla 20 etiket seçilebilir.");
        When(x => x.OpeningBalance is not null, () =>
        {
            RuleFor(x => x.OpeningBalance!.Amount).ValidMoney().OverridePropertyName("openingBalance.amount");
            RuleFor(x => x.OpeningBalance!.Direction).IsInEnum().OverridePropertyName("openingBalance.direction")
                .WithMessage("Bakiyenin yönünü seçin.");
        });
    }
}

public sealed class NoteRequestValidator : AbstractValidator<NoteRequest>
{
    public NoteRequestValidator() => RuleFor(x => x.Body)
        .Must(b => !string.IsNullOrWhiteSpace(b)).WithMessage("Not yazın.")
        .MaximumLength(2000).WithMessage("Not en fazla 2000 karakter olabilir.");
}

/// <summary>Arayüzdeki Türkçe karşılıklar (Excel şablonu ve dışa aktarma da kullanır).</summary>
public static class ContactLabels
{
    public static readonly IReadOnlyDictionary<ContactSource, string> Sources = new Dictionary<ContactSource, string>
    {
        [ContactSource.Instagram] = "Instagram",
        [ContactSource.Google] = "Google",
        [ContactSource.Referral] = "Tavsiye",
        [ContactSource.WalkIn] = "Yoldan geçerken",
        [ContactSource.Facebook] = "Facebook",
        [ContactSource.TikTok] = "TikTok",
        [ContactSource.Other] = "Diğer",
        [ContactSource.OnlineBooking] = "Online randevu",
    };

    public static ContactSource? ParseSource(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        foreach (var (source, label) in Sources)
        {
            if (string.Equals(label, t, StringComparison.InvariantCultureIgnoreCase) || string.Equals(source.ToString(), t, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        return null;
    }
}
