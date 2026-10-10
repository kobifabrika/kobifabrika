using Microsoft.AspNetCore.Identity;

namespace KobiFabrika.Infrastructure.Identity;

/// <summary>Identity hata mesajlarının Türkçesi. Kullanıcıya doğrudan gösterilir.</summary>
public sealed class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Beklenmeyen bir hata oluştu.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Kayıt başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Mevcut şifre hatalı.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Bağlantı geçersiz veya süresi dolmuş. Yeni bir bağlantı isteyin.");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "Kurtarma kodu geçersiz.");
    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), "Bu hesap başka bir kullanıcıya bağlı.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "Geçersiz kullanıcı adı.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Geçerli bir e-posta adresi girin.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Bu e-posta adresiyle kayıtlı bir hesap zaten var.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Bu e-posta adresiyle kayıtlı bir hesap zaten var.");
    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), "Geçersiz rol adı.");
    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), "Bu rol zaten var.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "Bu kullanıcının zaten bir şifresi var.");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "Bu kullanıcı için kilitleme kapalı.");
    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), "Kullanıcı zaten bu rolde.");
    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), "Kullanıcı bu rolde değil.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Şifre en az {length} karakter olmalı.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"Şifre en az {uniqueChars} farklı karakter içermeli.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Şifre en az bir sembol içermeli.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Şifre en az bir rakam içermeli.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Şifre en az bir küçük harf içermeli.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Şifre en az bir büyük harf içermeli.");

    public static IdentityError PasswordRequiresLetter() => Error(nameof(PasswordRequiresLetter), "Şifre en az bir harf içermeli.");
}
