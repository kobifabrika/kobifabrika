using KobiFabrika.Infrastructure.Security;

namespace KobiFabrika.Infrastructure.Businesses;

/// <summary>Davet token'ı (<see cref="SecureTokens"/>): 32 bayt rastgele, Base64Url. Veritabanına sadece SHA-256 özeti yazılır.</summary>
public static class InvitationTokens
{
    public static (string Token, byte[] Hash) Create() => SecureTokens.Create();

    public static byte[] Hash(string token) => SecureTokens.Hash(token);
}
