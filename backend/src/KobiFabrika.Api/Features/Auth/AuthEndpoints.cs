using System.Text;
using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Users;
using KobiFabrika.Infrastructure;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Auth;

/// <summary>
/// TermsAccepted: "Kullanım koşullarını ve Veri işleme sözleşmesini kabul ediyorum" (zorunlu); KVKK aydınlatma metni linkle
/// bilgilendirmedir. Plan/Period: fiyat sayfasından gelen paket tercihi (?paket=kalfa&amp;donem=yillik); geçersizse yok sayılır.
/// </summary>
public sealed record RegisterRequest(
    string FullName, string Email, string Phone, string Password, bool TermsAccepted, string? Plan = null, string? Period = null);

/// <summary>Bir sonraki girişte yeniden onay (Sahip; sürümü değişen Kullanım koşulları ve Veri işleme sözleşmesi).</summary>
public sealed record ConsentRequest(bool TermsAccepted);
public sealed record LoginRequest(string Email, string Password, bool RememberMe);
public sealed record ConfirmEmailRequest(Guid UserId, string Token);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record UpdateProfileRequest(string FullName, string Phone);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record SwitchBusinessRequest(Guid BusinessId);

/// <summary>KVKK aydınlatma metninin sürümü; metin değişince artırılır (frontend src/content/legal.ts → kvkk.version).</summary>
public static class KvkkNotice
{
    public const string Version = "2026-10-20";
}

public static class AuthEndpoints
{
    public const string AuthRateLimit = "auth";
    public const string ResendRateLimit = "resend-confirmation";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting(AuthRateLimit)
            .Validate<RegisterRequest>().WithSummary("Hesap oluştur (Owner olacak kullanıcı), oturum aç ve doğrulama e-postası gönder");
        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(AuthRateLimit)
            .Validate<LoginRequest>().WithSummary("Giriş; rememberMe ise 30 gün, değilse tarayıcı kapanınca biter");
        group.MapPost("/logout", Logout).RequireAuthorization().WithSummary("Çıkış");
        group.MapGet("/me", Me).RequireAuthorization().WithSummary("Oturumdaki kullanıcı, aktif işletme ve rol");
        group.MapPost("/switch-business", SwitchBusiness).RequireAuthorization().WithSummary("Aktif işletmeyi değiştir");
        group.MapPost("/confirm-email", ConfirmEmail).AllowAnonymous().RequireRateLimiting(AuthRateLimit)
            .WithSummary("E-posta doğrulama linkindeki token'ı onayla");
        group.MapPost("/resend-confirmation", ResendConfirmation).RequireAuthorization().RequireRateLimiting(ResendRateLimit)
            .WithSummary("Doğrulama e-postasını tekrar gönder (60 sn'de bir)");
        group.MapPost("/forgot-password", ForgotPassword).AllowAnonymous().RequireRateLimiting(AuthRateLimit)
            .Validate<ForgotPasswordRequest>().WithSummary("Şifre sıfırlama e-postası (hesap olsun olmasın 204)");
        group.MapPost("/reset-password", ResetPassword).AllowAnonymous().RequireRateLimiting(AuthRateLimit)
            .Validate<ResetPasswordRequest>().WithSummary("Yeni şifre belirle; diğer oturumlar düşer");
        group.MapPost("/consents", AcceptConsents).RequireAuthorization().Validate<ConsentRequest>()
            .WithSummary("Güncellenen Kullanım koşulları ve Veri işleme sözleşmesini onayla");
        group.MapPut("/profile", UpdateProfile).RequireAuthorization().Validate<UpdateProfileRequest>().WithSummary("Ad ve telefonu güncelle");
        group.MapPost("/change-password", ChangePassword).RequireAuthorization().Validate<ChangePasswordRequest>().WithSummary("Şifre değiştir");

        return app;
    }

    private static async Task<Results<Ok<MeResponse>, ValidationProblem>> Register(
        RegisterRequest request,
        UserManager<AppUser> users,
        SessionService session,
        AppDbContext db,
        AccountEmails emails,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        var email = Validation.NormalizeEmail(request.Email);
        var user = new AppUser
        {
            FullName = request.FullName.Trim(),
            UserName = email,
            Email = email,
            PhoneNumber = Validation.NormalizePhone(request.Phone),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            LastLoginAt = clock.GetUtcNow().UtcDateTime, // kayıt otomatik oturum açar: ilk giriş sayılır (süper admin "Son giriş")
            RequestedPlanCode = PlanCodes.IsValid(request.Plan) ? request.Plan : null,
            RequestedPeriod = PlanCodes.IsValid(request.Plan) && PlanPeriods.IsValid(request.Period) ? request.Period : null,
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return (ValidationProblem)Validation.IdentityProblem(result);
        }

        db.UserConsents.AddRange(Consents(user.Id, clock, http, owner: true));
        await db.SaveChangesAsync(ct);

        await emails.SendConfirmationAsync(user, ct);
        await session.SignInAsync(user, persistent: false, ct);
        return TypedResults.Ok(await session.BuildMeAsync(user, null, ct));
    }

    /// <summary>
    /// Kayıtta yazılan onaylar: KVKK bilgilendirmesi ve Kullanım koşulları; işletme kuracak kullanıcıda (owner) Veri işleme
    /// sözleşmesi de. Davetle gelen çalışan veri işleme sözleşmesinin tarafı değildir.
    /// </summary>
    internal static IEnumerable<UserConsent> Consents(Guid userId, TimeProvider clock, HttpContext http, bool owner)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ip = http.Connection.RemoteIpAddress?.ToString();
        UserConsent Make(UserConsentKind kind, string version) =>
            new() { UserId = userId, Kind = kind, Version = version, AcceptedAt = now, IpAddress = ip };

        yield return Make(UserConsentKind.KvkkPrivacyNotice, KvkkNotice.Version);
        yield return Make(UserConsentKind.TermsOfUse, LegalVersions.TermsOfUse);
        if (owner)
        {
            yield return Make(UserConsentKind.DataProcessingAgreement, LegalVersions.DataProcessingAgreement);
        }
    }

    /// <summary>Sahip için güncel sürümlerin eksik olanları yazılır (tekrar çağrılırsa yeni satır açmaz).</summary>
    private static async Task<Ok<MeResponse>> AcceptConsents(
        ConsentRequest request, UserManager<AppUser> users, SessionService session, AppDbContext db, TimeProvider clock, HttpContext http,
        CancellationToken ct)
    {
        var user = await users.GetUserAsync(http.User) ?? throw new UnauthorizedAccessException();
        var have = await db.UserConsents.Where(c => c.UserId == user.Id).Select(c => new { c.Kind, c.Version }).ToListAsync(ct);
        foreach (var consent in Consents(user.Id, clock, http, owner: true))
        {
            if (consent.Kind != UserConsentKind.KvkkPrivacyNotice && !have.Any(h => h.Kind == consent.Kind && h.Version == consent.Version))
            {
                db.UserConsents.Add(consent);
            }
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await session.BuildMeAsync(user, user.LastBusinessId, ct));
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> Login(
        LoginRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        SessionService session,
        TenantContext tenant,
        IAuditLogger audit,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(Validation.NormalizeEmail(request.Email));
        if (user is null)
        {
            return InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Çok fazla hatalı deneme. 5 dakika sonra tekrar dene.",
                extensions: new Dictionary<string, object?> { ["code"] = "locked_out" });
        }

        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        await session.SignInAsync(user, request.RememberMe, ct);
        var loggedInAt = clock.GetUtcNow().UtcDateTime;
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, loggedInAt), ct);

        // Zaten açık bir oturumla başka işletmeye girişte kiracı bağlamı değiştirilemez; günlük o durumda atlanır.
        if (user.LastBusinessId is { } businessId && (tenant.BusinessId is null || tenant.BusinessId == businessId))
        {
            tenant.Set(businessId, user.Id);
            audit.Log(AuditAction.Login);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Aynı kullanıcı aynı anda iki kez giriş yaptı (çift tıklama, paralel sekme): kullanıcı satırı diğer istekte
                // güncellendi. Giriş zaten tamam; sadece günlük satırı yeniden yazılır (Sprint 6A'da E2E'de bulundu).
                db.ChangeTracker.Clear();
                audit.Log(AuditAction.Login);
                await db.SaveChangesAsync(ct);
            }
        }

        return TypedResults.Ok(await session.BuildMeAsync(user, user.LastBusinessId, ct));
    }

    private static ProblemHttpResult InvalidCredentials() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        detail: "E-posta veya şifre hatalı.",
        extensions: new Dictionary<string, object?> { ["code"] = "invalid_credentials" });

    private static async Task<NoContent> Logout(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> Me(
        CurrentMember member,
        UserManager<AppUser> users,
        SessionService session,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(member.RequiredUserId.ToString());
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var businessId = SessionService.ActiveBusinessOf(member);
        if (businessId is null)
        {
            // Aktif işletmedeki üyelik kaldırıldıysa başka aktif üyeliğe geçilir.
            businessId = await session.EnsureActiveBusinessAsync(user, ct);
            if (businessId is not null)
            {
                await session.SwitchBusinessAsync(user, businessId.Value);
            }
        }

        return TypedResults.Ok(await session.BuildMeAsync(user, businessId, ct));
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> SwitchBusiness(
        SwitchBusinessRequest request,
        CurrentMember member,
        UserManager<AppUser> users,
        SessionService session,
        AppDbContext db,
        CancellationToken ct)
    {
        var userId = member.RequiredUserId;
        var isMember = await db.BusinessMemberships
            .AnyAsync(m => m.UserId == userId && m.BusinessId == request.BusinessId && m.IsActive, ct);
        if (!isMember)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "Bu işletmeye erişiminiz yok.");
        }

        var user = (await users.FindByIdAsync(userId.ToString()))!;
        await session.SwitchBusinessAsync(user, request.BusinessId);
        return TypedResults.Ok(await session.BuildMeAsync(user, request.BusinessId, ct));
    }

    private static async Task<Results<NoContent, ValidationProblem>> ConfirmEmail(
        ConfirmEmailRequest request,
        UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || !TryDecode(request.Token, out var token))
        {
            return (ValidationProblem)Validation.FieldProblem("token", new TurkishIdentityErrorDescriber().InvalidToken().Description);
        }

        if (user.EmailConfirmed)
        {
            return TypedResults.NoContent();
        }

        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? TypedResults.NoContent() : (ValidationProblem)Validation.IdentityProblem(result);
    }

    private static async Task<NoContent> ResendConfirmation(
        CurrentMember member,
        UserManager<AppUser> users,
        AccountEmails emails,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(member.RequiredUserId.ToString());
        if (user is { EmailConfirmed: false })
        {
            await emails.SendConfirmationAsync(user, ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ForgotPassword(
        ForgotPasswordRequest request,
        UserManager<AppUser> users,
        AccountEmails emails,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(Validation.NormalizeEmail(request.Email));
        if (user is not null)
        {
            await emails.SendPasswordResetAsync(user, ct);
        }

        // Hesap olup olmadığı belli edilmez.
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> ResetPassword(
        ResetPasswordRequest request,
        UserManager<AppUser> users,
        AccountEmails emails,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(Validation.NormalizeEmail(request.Email));
        if (user is null || !TryDecode(request.Token, out var token))
        {
            return (ValidationProblem)Validation.FieldProblem("token", new TurkishIdentityErrorDescriber().InvalidToken().Description);
        }

        // ResetPasswordAsync security stamp'i yeniler: diğer cihazlardaki oturumlar düşer.
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return (ValidationProblem)Validation.IdentityProblem(result);
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await emails.SendPasswordChangedAsync(user, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MeUser>, ValidationProblem>> UpdateProfile(
        UpdateProfileRequest request,
        CurrentMember member,
        UserManager<AppUser> users,
        PlatformAdmins admins)
    {
        var user = (await users.FindByIdAsync(member.RequiredUserId.ToString()))!;
        user.FullName = request.FullName.Trim();
        user.PhoneNumber = Validation.NormalizePhone(request.Phone);

        var result = await users.UpdateAsync(user);
        return result.Succeeded
            // ConsentRequired false: onay penceresi paneli kapatır; onaylanmadan profil ekranına gelinmez.
            ? TypedResults.Ok(new MeUser(user.Id, user.FullName, user.Email!, user.EmailConfirmed, user.PhoneNumber, await admins.IsAdminAsync(user.Id),
                ConsentRequired: false))
            : (ValidationProblem)Validation.IdentityProblem(result);
    }

    private static async Task<Results<NoContent, ValidationProblem>> ChangePassword(
        ChangePasswordRequest request,
        CurrentMember member,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AccountEmails emails,
        CancellationToken ct)
    {
        var user = (await users.FindByIdAsync(member.RequiredUserId.ToString()))!;
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return (ValidationProblem)Validation.IdentityProblem(result);
        }

        // Diğer oturumlar düşer, bu oturum yeni security stamp ile devam eder.
        await signIn.RefreshSignInAsync(user);
        await emails.SendPasswordChangedAsync(user, ct);
        return TypedResults.NoContent();
    }

    private static bool TryDecode(string? token, out string decoded)
    {
        decoded = "";
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
}

/// <summary>Hesap e-postaları: doğrulama, şifre sıfırlama, şifre değişti bildirimi.</summary>
public sealed class AccountEmails(UserManager<AppUser> users, EmailDispatch dispatch, IOptions<AppOptions> app)
{
    public async Task SendConfirmationAsync(AppUser user, CancellationToken ct)
    {
        var token = AuthEndpoints.Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = app.Value.Link($"/e-posta-dogrula?u={user.Id}&t={token}");
        await dispatch.TrySendAsync(EmailTemplates.ConfirmEmail(user.Email!, user.FullName, link), ct);
    }

    public async Task SendPasswordResetAsync(AppUser user, CancellationToken ct)
    {
        var token = AuthEndpoints.Encode(await users.GeneratePasswordResetTokenAsync(user));
        var link = app.Value.Link($"/sifre-sifirla?e={Uri.EscapeDataString(user.Email!)}&t={token}");
        await dispatch.TrySendAsync(EmailTemplates.ResetPassword(user.Email!, user.FullName, link), ct);
    }

    public Task SendPasswordChangedAsync(AppUser user, CancellationToken ct) =>
        dispatch.TrySendAsync(EmailTemplates.PasswordChanged(user.Email!, user.FullName), ct);
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).ValidFullName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Phone).ValidPhone();
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.TermsAccepted).Equal(true).WithMessage("Devam etmek için Kullanım koşullarını ve Veri işleme sözleşmesini kabul edin.");
    }
}

public sealed class ConsentRequestValidator : AbstractValidator<ConsentRequest>
{
    public ConsentRequestValidator() =>
        RuleFor(x => x.TermsAccepted).Equal(true).WithMessage("Devam etmek için Kullanım koşullarını ve Veri işleme sözleşmesini kabul edin.");
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("E-posta adresi girin.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Şifre girin.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).ValidEmail();
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Token).NotEmpty().WithMessage("Bağlantı geçersiz.");
        RuleFor(x => x.NewPassword).ValidPassword();
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).ValidFullName();
        RuleFor(x => x.Phone).ValidPhone();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Mevcut şifreyi girin.");
        RuleFor(x => x.NewPassword).ValidPassword();
    }
}
