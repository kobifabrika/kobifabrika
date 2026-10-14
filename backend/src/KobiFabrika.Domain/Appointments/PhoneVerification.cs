using System.Security.Cryptography;
using System.Text;
using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Appointments;

/// <summary>
/// Online randevuda telefon doğrulama kodu. Kod 6 hane, veritabanında sadece SHA-256(kod + Id) özeti; 5 dakika geçerli,
/// en fazla 3 yanlış deneme, tek kullanımlık. Doğrulanmış (ama randevuya dönüşmemiş, ör. saat doldu) kod 15 dakika içinde
/// başka saatle yeniden kullanılabilir. 7 günden eski satırları saatlik iş siler.
/// </summary>
public class PhoneVerification : TenantEntity
{
    public const int CodeLength = 6;
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan VerifiedReuse = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>Aynı numaraya (bütün işletmeler) saatte en fazla bu kadar kod.</summary>
    public const int HourlyPerPhone = 3;

    /// <summary>E.164 (+905…).</summary>
    public required string Phone { get; set; }

    public required byte[] CodeHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? IpAddress { get; set; }
    public Guid? AppointmentId { get; set; }

    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static byte[] Hash(Guid id, string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim() + id.ToString("N")));

    public bool Matches(string code) => CryptographicOperations.FixedTimeEquals(Hash(Id, code), CodeHash);

    /// <summary>Türkiye cep telefonu mu (+905XXXXXXXXX).</summary>
    public static bool IsMobile(string e164) => e164.StartsWith("+905", StringComparison.Ordinal) && e164.Length == 13;
}
