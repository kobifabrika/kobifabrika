using Microsoft.AspNetCore.Identity;

namespace KobiFabrika.Infrastructure.Identity;

/// <summary>Şifre kuralı: en az bir harf (Türkçe harfler dahil). Uzunluk ve rakam Identity seçeneklerinde.</summary>
public sealed class LetterPasswordValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password) =>
        Task.FromResult(password is not null && password.Any(char.IsLetter)
            ? IdentityResult.Success
            : IdentityResult.Failed(TurkishIdentityErrorDescriber.PasswordRequiresLetter()));
}
