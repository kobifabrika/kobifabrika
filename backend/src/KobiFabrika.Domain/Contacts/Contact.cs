using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Contacts;

/// <summary>
/// Cari kart: müşteri ve/veya tedarikçi (aynı tablo). Ad, soyad veya firma adından en az biri zorunlu.
/// Telefon E.164 biçiminde ve işletme içinde benzersiz (silinmemiş kayıtlar arasında).
/// </summary>
public class Contact : TenantEntity
{
    public bool IsCustomer { get; set; } = true;
    public bool IsSupplier { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? CompanyName { get; set; }

    /// <summary>Listede ve aramada görünen ad: "Ad Soyad", yoksa firma adı. <see cref="RefreshDisplayName"/> ile dolar.</summary>
    public string DisplayName { get; set; } = "";

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public ContactSource? Source { get; set; }

    /// <summary>Karttaki kısa sabit not (zaman çizelgesindeki notlardan ayrı).</summary>
    public string? Notes { get; set; }

    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }

    /// <summary>
    /// Cari bakiye önbelleği = Σ(Borç − Alacak). Pozitif: müşteri bize borçlu ("Alacak"), negatif: biz borçluyuz ("Borç").
    /// Sadece SQL tarafında atomik olarak güncellenir (LedgerService); SaveChanges bu alanı yazmaz.
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>Son ziyaret (satış/randevu ile Sprint 3/5'te dolar).</summary>
    public DateTime? LastVisitAt { get; set; }

    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // Güncel KVKK izinleri (geçmiş ContactConsents'te).
    public bool SmsConsent { get; set; }
    public bool EmailConsent { get; set; }
    public bool CallConsent { get; set; }

    public bool HasConsent(ConsentChannel channel) => channel switch
    {
        ConsentChannel.Sms => SmsConsent,
        ConsentChannel.Email => EmailConsent,
        _ => CallConsent,
    };

    public void SetConsent(ConsentChannel channel, bool granted)
    {
        switch (channel)
        {
            case ConsentChannel.Sms: SmsConsent = granted; break;
            case ConsentChannel.Email: EmailConsent = granted; break;
            default: CallConsent = granted; break;
        }
    }

    public void RefreshDisplayName()
    {
        var person = string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        DisplayName = person.Length > 0 ? person : CompanyName?.Trim() ?? "";
    }
}
