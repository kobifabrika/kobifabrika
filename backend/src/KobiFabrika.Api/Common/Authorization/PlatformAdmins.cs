using System.Security.Claims;
using KobiFabrika.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace KobiFabrika.Api.Common.Authorization;

/// <summary>"Platform" bölümü: KobiFabrika ekibinin e-postaları (user-secrets / ortam değişkeni; geliştirmede emirkayaco@gmail.com).</summary>
public sealed class PlatformOptions
{
    public const string Section = "Platform";

    public string[] AdminEmails { get; set; } = [];
}

/// <summary>
/// PlatformAdmin: Identity rolü <see cref="AppRole.PlatformAdmin"/> veya e-postası <c>Platform:AdminEmails</c>'te olan kullanıcı.
/// Rol cookie'de tutulmaz; her istekte okunur (rol değişikliği bir sonraki istekte geçerli).
/// </summary>
public sealed class PlatformAdmins(UserManager<AppUser> users, Microsoft.Extensions.Options.IOptions<PlatformOptions> options)
{
    public async Task<bool> IsAdminAsync(Guid userId, ClaimsPrincipal? principal = null)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var emails = options.Value.AdminEmails;
        if (user.Email is { } email && user.EmailConfirmed && emails.Any(e => string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return await users.IsInRoleAsync(user, AppRole.PlatformAdmin);
    }
}
