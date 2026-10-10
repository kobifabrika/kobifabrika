using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Imports;

public enum ImportJobStatus
{
    Uploaded,
    Completed,
}

/// <summary>
/// Excel içe aktarma oturumu: yüklenen dosyanın başlıkları ve satırları (JSON) önizleme → kaydet arasında burada durur.
/// 24 saatten eski oturumlar arka plan işiyle silinir.
/// </summary>
public class ImportJob : TenantEntity
{
    public const int MaxRows = 5000;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public required string FileName { get; set; }
    public ImportJobStatus Status { get; set; }

    /// <summary>Başlıklar (JSON dizi).</summary>
    public required string Headers { get; set; }

    /// <summary>Satırlar (JSON: metin hücre dizilerinin dizisi).</summary>
    public required string Rows { get; set; }

    /// <summary>Kaydetme raporu (JSON).</summary>
    public string? Result { get; set; }

    public DateTime ExpiresAt { get; set; }
}
