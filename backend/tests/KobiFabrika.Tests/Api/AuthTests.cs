using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Domain.Users;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Tests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KobiFabrika.Tests.Api;

public sealed class AuthTests(SqlServerFixture db) : ApiTestBase(db)
{
    [Fact]
    public async Task Register_creates_user_with_kvkk_consent_signs_in_and_sends_confirmation_email()
    {
        var user = await RegisterAsync(fullName: "Ayşe Yılmaz");

        var me = await GetJsonAsync(user.Client, "/api/auth/me");
        me.GetProperty("user").GetProperty("fullName").GetString().ShouldBe("Ayşe Yılmaz");
        me.GetProperty("user").GetProperty("phone").GetString().ShouldBe("+905321234567");
        me.GetProperty("user").GetProperty("emailConfirmed").GetBoolean().ShouldBeFalse();
        me.GetProperty("business").ValueKind.ShouldBe(JsonValueKind.Null);

        var email = Factory.Emails.To(user.Email).ShouldHaveSingleItem();
        email.Subject.ShouldBe("E-posta adresini doğrula");
        email.TextBody.ShouldContain("http://localhost:3000/e-posta-dogrula?u=");

        // Kayıtta: KVKK bilgilendirmesi, Kullanım koşulları ve Veri işleme sözleşmesi (sürümleriyle); yeniden onay istenmez.
        using var scope = Factory.Services.CreateScope();
        var consents = await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserConsents
            .Where(c => c.UserId == user.UserId).ToListAsync(Ct);
        consents.Select(c => (c.Kind, c.Version)).ShouldBe([
            (UserConsentKind.KvkkPrivacyNotice, KvkkNotice.Version),
            (UserConsentKind.TermsOfUse, LegalVersions.TermsOfUse),
            (UserConsentKind.DataProcessingAgreement, LegalVersions.DataProcessingAgreement),
        ], ignoreOrder: true);
        me.GetProperty("user").GetProperty("consentRequired").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Owner_without_current_terms_is_asked_once_and_consent_is_recorded()
    {
        var owner = await CreateOwnerAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            // Eski sürümle kaydolmuş Sahip: güncel sürümlü onayları yok.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UserConsents.Where(c => c.UserId == owner.UserId && c.Kind != UserConsentKind.KvkkPrivacyNotice).ExecuteDeleteAsync(Ct);
        }

        var me = await owner.Client.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        me.GetProperty("user").GetProperty("consentRequired").GetBoolean().ShouldBeTrue();

        var refused = await owner.Client.PostAsJsonAsync("/api/auth/consents", new { termsAccepted = false }, Ct);
        (await ProblemAsync(refused, HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("termsAccepted")[0].GetString()
            .ShouldBe("Devam etmek için Kullanım koşullarını ve Veri işleme sözleşmesini kabul edin.");

        var accepted = await owner.Client.PostAsJsonAsync("/api/auth/consents", new { termsAccepted = true }, Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("user").GetProperty("consentRequired").GetBoolean().ShouldBeFalse();
        (await owner.Client.PostAsJsonAsync("/api/auth/consents", new { termsAccepted = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var check = Factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().UserConsents
            .CountAsync(c => c.UserId == owner.UserId && c.Kind != UserConsentKind.KvkkPrivacyNotice, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Register_without_terms_or_with_weak_password_returns_turkish_errors()
    {
        var response = await NewClient().PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "E",
            email = "gecersiz",
            phone = "123",
            password = "sadeceharf",
            termsAccepted = false,
        }, Ct);

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors");
        errors.GetProperty("termsAccepted")[0].GetString().ShouldBe("Devam etmek için Kullanım koşullarını ve Veri işleme sözleşmesini kabul edin.");
        errors.GetProperty("password")[0].GetString().ShouldBe("Şifre en az bir rakam içermeli.");
        errors.GetProperty("email")[0].GetString().ShouldBe("Geçerli bir e-posta adresi girin.");
        errors.GetProperty("phone")[0].GetString().ShouldStartWith("Geçerli bir telefon numarası girin");
        errors.GetProperty("fullName")[0].GetString().ShouldBe("Ad soyad girin.");
    }

    [Fact]
    public async Task Register_with_existing_email_is_rejected()
    {
        var user = await RegisterAsync();

        var response = await NewClient().PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Başka Biri",
            email = user.Email.ToUpperInvariant(),
            phone = "05321234567",
            password = DefaultPassword,
            termsAccepted = true,
        }, Ct);

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").GetProperty("email")[0].GetString().ShouldBe("Bu e-posta adresiyle kayıtlı bir hesap zaten var.");
    }

    [Fact]
    public async Task Login_works_without_email_confirmation()
    {
        var user = await RegisterAsync();
        var client = NewClient();

        var response = await LoginAsync(client, user.Email);

        await EnsureSuccessAsync(response);
        (await GetJsonAsync(client, "/api/auth/me")).GetProperty("user").GetProperty("email").GetString().ShouldBe(user.Email);
    }

    [Fact]
    public async Task Remember_me_sets_persistent_30_day_cookie_otherwise_session_cookie()
    {
        var user = await RegisterAsync();

        var remembered = await LoginAsync(NewClient(), user.Email, rememberMe: true);
        var session = await LoginAsync(NewClient(), user.Email, rememberMe: false);

        var persistentCookie = AuthCookie(remembered);
        persistentCookie.ShouldContain("expires=", Case.Insensitive);
        var expires = DateTimeOffset.Parse(persistentCookie.Split(';').First(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Split('=')[1]);
        (expires - Factory.Clock.GetUtcNow()).TotalDays.ShouldBeInRange(29.9, 30.1);

        AuthCookie(session).ShouldNotContain("expires=", Case.Insensitive);
        AuthCookie(session).ShouldContain("httponly", Case.Insensitive);
    }

    private static string AuthCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("kf.auth=", StringComparison.Ordinal));

    [Fact]
    public async Task Wrong_password_returns_turkish_error_and_locks_out_after_five_attempts()
    {
        var user = await RegisterAsync();
        var client = NewClient();

        var first = await ProblemAsync(await LoginAsync(client, user.Email, "Yanlis1234"), HttpStatusCode.Unauthorized);
        first.GetProperty("detail").GetString().ShouldBe("E-posta veya şifre hatalı.");

        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(client, user.Email, "Yanlis1234");
        }

        var locked = await ProblemAsync(await LoginAsync(client, user.Email), HttpStatusCode.Unauthorized);
        locked.GetProperty("code").GetString().ShouldBe("locked_out");
    }

    [Fact]
    public async Task Unknown_email_returns_same_error_as_wrong_password()
    {
        var problem = await ProblemAsync(await LoginAsync(NewClient(), UniqueEmail()), HttpStatusCode.Unauthorized);

        problem.GetProperty("detail").GetString().ShouldBe("E-posta veya şifre hatalı.");
    }

    [Fact]
    public async Task Email_link_confirms_email()
    {
        var user = await RegisterAsync();

        await ConfirmEmailAsync(user);

        (await GetJsonAsync(user.Client, "/api/auth/me")).GetProperty("user").GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_confirmation_token_returns_turkish_error()
    {
        var user = await RegisterAsync();

        var response = await NewClient().PostAsJsonAsync("/api/auth/confirm-email", new { userId = user.UserId, token = "bozuk" }, Ct);

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("errors").GetProperty("token")[0].GetString().ShouldStartWith("Bağlantı geçersiz");
    }

    [Fact]
    public async Task Resend_confirmation_is_limited_to_once_per_minute()
    {
        var user = await RegisterAsync();

        (await user.Client.PostAsync("/api/auth/resend-confirmation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.Client.PostAsync("/api/auth/resend-confirmation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        Factory.Emails.To(user.Email).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Forgot_password_returns_204_for_unknown_email_without_sending()
    {
        var email = UniqueEmail();

        var response = await NewClient().PostAsJsonAsync("/api/auth/forgot-password", new { email }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Factory.Emails.To(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task Password_reset_changes_password_and_invalidates_other_sessions()
    {
        var user = await RegisterAsync();
        (await NewClient().PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var query = QueryHelpers.ParseQuery(new Uri(new Uri("https://x"), Factory.Emails.LinkTo(user.Email, "/sifre-sifirla")).Query);
        query["e"].ToString().ShouldBe(user.Email);
        var reset = await NewClient().PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = user.Email,
            token = query["t"].ToString(),
            newPassword = "YeniSifre99",
        }, Ct);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Factory.Emails.To(user.Email).Last().Subject.ShouldBe("Şifren değiştirildi");
        (await LoginAsync(NewClient(), user.Email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginAsync(NewClient(), user.Email, "YeniSifre99")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Security stamp kontrolü 5 dakikada bir: süre geçince eski cookie geçersizdir.
        Factory.Clock.Advance(TimeSpan.FromMinutes(6));
        (await user.Client.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_keeps_current_session()
    {
        var user = await RegisterAsync();

        var response = await user.Client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = DefaultPassword, newPassword = "Degisti123" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Factory.Clock.Advance(TimeSpan.FromMinutes(6));
        (await user.Client.GetAsync("/api/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var wrong = await user.Client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Yanlis123", newPassword = "Degisti456" }, Ct);
        (await ProblemAsync(wrong, HttpStatusCode.BadRequest)).GetProperty("errors").GetProperty("currentPassword")[0].GetString().ShouldBe("Mevcut şifre hatalı.");
    }

    [Fact]
    public async Task Profile_update_normalizes_phone()
    {
        var user = await RegisterAsync();

        var response = await user.Client.PutAsJsonAsync("/api/auth/profile", new { fullName = "  Emir  Kaya ", phone = "+90 (212) 555 44 33" }, Ct);

        await EnsureSuccessAsync(response);
        var me = await GetJsonAsync(user.Client, "/api/auth/me");
        me.GetProperty("user").GetProperty("phone").GetString().ShouldBe("+902125554433");
    }

    [Fact]
    public async Task Logout_ends_session()
    {
        var user = await RegisterAsync();

        (await user.Client.PostAsync("/api/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var problem = await ProblemAsync(await user.Client.GetAsync("/api/auth/me", Ct), HttpStatusCode.Unauthorized);
        problem.GetProperty("code").GetString().ShouldBe("unauthenticated");
    }

    [Fact]
    public async Task Panel_endpoints_require_completed_setup()
    {
        var anonymous = await ProblemAsync(await NewClient().GetAsync("/api/search?q=ab", Ct), HttpStatusCode.Unauthorized);
        anonymous.GetProperty("title").GetString().ShouldBe("Oturum açmanız gerekiyor");

        var user = await RegisterAsync();
        var problem = await ProblemAsync(await user.Client.GetAsync("/api/search?q=ab", Ct), HttpStatusCode.Forbidden);
        problem.GetProperty("code").GetString().ShouldBe("setup_required");
    }
}

/// <summary>Kayıt/giriş uçları IP başına dakikada sınırlıdır.</summary>
public sealed class AuthRateLimitTests(SqlServerFixture db) : ApiTestBase(db)
{
    protected override KobiFabrikaApiFactory CreateFactory() => new(Db) { AuthPermitLimit = 3 };

    [Fact]
    public async Task Login_attempts_are_rate_limited()
    {
        var client = NewClient();
        for (var i = 0; i < 3; i++)
        {
            (await LoginAsync(client, "yok@test.com")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await LoginAsync(client, "yok@test.com")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
