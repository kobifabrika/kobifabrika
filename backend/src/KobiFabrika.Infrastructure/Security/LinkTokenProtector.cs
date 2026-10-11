using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace KobiFabrika.Infrastructure.Security;

/// <summary>
/// Herkese açık link token'ını geri çözülebilir biçimde saklar (aynı linki tekrar göstermek için). Doğrulama her zaman
/// SHA-256 özetiyle yapılır; şifreli değer sadece panelde linki yeniden göstermeye yarar.
/// </summary>
public sealed class LinkTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("KobiFabrika.LinkTokens.v1");

    public string Protect(string token) => _protector.Protect(token);

    /// <summary>Çözülemezse (anahtar yok, bozuk değer) veya özet tutmazsa null; çağıran yeni token üretir.</summary>
    public string? TryUnprotect(string? protectedToken, byte[] expectedHash)
    {
        if (string.IsNullOrEmpty(protectedToken))
        {
            return null;
        }

        try
        {
            var token = _protector.Unprotect(protectedToken);
            return CryptographicOperations.FixedTimeEquals(SecureTokens.Hash(token), expectedHash) ? token : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
