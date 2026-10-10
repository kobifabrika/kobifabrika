using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace KobiFabrika.Infrastructure.Security;

/// <summary>
/// Herkese açık link token'ları (davet, ekstre, tahsilat linki): 32 bayt rastgele, Base64Url (43 karakter).
/// Veritabanına sadece SHA-256 özeti yazılır; tahmin edilemez, süreli ve kayıt iptal edilerek geri alınabilir.
/// </summary>
public static class SecureTokens
{
    public const int MaxLength = 100;

    public static (string Token, byte[] Hash) Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>Biçimce olası mı (boş/çok uzun token veritabanına sorulmaz).</summary>
    public static bool LooksValid(string? token) => !string.IsNullOrWhiteSpace(token) && token.Length <= MaxLength;
}
