namespace KobiFabrika.Domain.Common;

public enum DocumentKind
{
    Sale,
    Quote,
}

/// <summary>
/// İşletme ve yıl bazında belge sayacı (SAT-2026-000001, TKL-2026-0001). Sıradaki değer belgeyle aynı transaction'da
/// atomik UPDATE … OUTPUT ile alınır: numaralar boşluksuz ve tekrarsızdır. Soft delete ve audit alanı yoktur.
/// </summary>
public class DocumentSequence : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public DocumentKind Kind { get; set; }
    public int Year { get; set; }
    public int LastValue { get; set; }
}

/// <summary>Belge numarası biçimi. Yıl işletmenin yerel saatine göredir.</summary>
public static class DocumentNumber
{
    public static string Format(DocumentKind kind, int year, int sequence) => kind switch
    {
        DocumentKind.Sale => $"SAT-{year}-{sequence:D6}",
        DocumentKind.Quote => $"TKL-{year}-{sequence:D4}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static int YearOf(DateTime utc) => BusinessTime.ToLocal(utc).Year;
}
