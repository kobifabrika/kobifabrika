using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace KobiFabrika.Infrastructure.Businesses;

/// <summary>Davet token'ı: 32 bayt rastgele, Base64Url. Veritabanına sadece SHA-256 özeti yazılır.</summary>
public static class InvitationTokens
{
    public static (string Token, byte[] Hash) Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
