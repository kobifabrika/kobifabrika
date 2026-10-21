using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Businesses;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Invitations;

public sealed record InvitationPreviewResponse(
    string BusinessName, BusinessRole Role, string InvitedBy, string Email, bool HasAccount, InvitationStatus Status);

public sealed record AcceptInvitationRegisterRequest(string FullName, string Phone, string Password, bool TermsAccepted);

/// <summary>
/// /davet/[token] sayfasının uçları. Kiracı token'dan çözülür (docs/02-mimari.md: herkese açık uçlar kiracıyı
/// token'dan kendileri çözer). Kabul işlemi davetin işletmesine ait yeni bir DI scope'unda yapılır; böylece
/// oturumdaki kullanıcının başka bir aktif işletmesi olsa da kiracı bağlamı karışmaz.
/// </summary>
public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invitations/{token}").WithTags("Invitations");

        group.MapGet("/", Preview).AllowAnonymous().RequireRateLimiting(AuthEndpoints.AuthRateLimit).WithSummary("Davet özeti");
        group.MapPost("/accept", Accept).RequireAuthorization().WithSummary("Oturumdaki kullanıcı daveti kabul eder");
        group.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting(AuthEndpoints.AuthRateLimit)
            .Validate<AcceptInvitationRegisterRequest>().WithSummary("Hesabı olmayan davetli kayıt olur ve daveti kabul eder");

        return app;
    }

    private static Task<Invitation?> FindAsync(AppDbContext db, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return Task.FromResult<Invitation?>(null);
        }

        var hash = InvitationTokens.Hash(token);
        return db.Invitations.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
    }

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Davet bulunamadı. Bağlantıyı kontrol edin.");

    private static ProblemHttpResult NotUsable(InvitationStatus status) => TypedResults.Problem(
        statusCode: StatusCodes.Status410Gone,
        detail: status switch
        {
            InvitationStatus.Accepted => "Bu davet zaten kullanılmış.",
            InvitationStatus.Revoked => "Bu davet iptal edilmiş.",
            _ => "Bu davetin süresi dolmuş. İşletmeden yeni bir davet isteyin.",
        },
        extensions: new Dictionary<string, object?> { ["code"] = $"invitation_{status.ToString().ToLowerInvariant()}" });

    private static async Task<Results<Ok<InvitationPreviewResponse>, ProblemHttpResult>> Preview(
        string token, AppDbContext db, UserManager<AppUser> users, TimeProvider clock, CancellationToken ct)
    {
        var invitation = await FindAsync(db, token, ct);
        if (invitation is null)
        {
            return NotFound();
        }

        var businessName = await db.Businesses.Where(b => b.Id == invitation.BusinessId).Select(b => b.Name).FirstAsync(ct);
        var invitedBy = await db.Users.Where(u => u.Id == invitation.InvitedByUserId).Select(u => u.FullName).FirstAsync(ct);
        var hasAccount = await users.FindByEmailAsync(invitation.Email) is not null;

        return TypedResults.Ok(new InvitationPreviewResponse(
            businessName, invitation.Role, invitedBy, invitation.Email, hasAccount, invitation.StatusAt(clock.GetUtcNow().UtcDateTime)));
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> Accept(
        string token,
        CurrentMember member,
        AppDbContext db,
        UserManager<AppUser> users,
        SessionService session,
        InvitationAcceptor acceptor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var invitation = await FindAsync(db, token, ct);
        if (invitation is null)
        {
            return NotFound();
        }

        var status = invitation.StatusAt(clock.GetUtcNow().UtcDateTime);
        if (status != InvitationStatus.Pending)
        {
            return NotUsable(status);
        }

        var user = (await users.FindByIdAsync(member.RequiredUserId.ToString()))!;
        if (!string.Equals(user.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                detail: $"Bu davet {invitation.Email} adresine gönderildi. O hesapla giriş yapıp tekrar deneyin.",
                extensions: new Dictionary<string, object?> { ["code"] = "invitation_email_mismatch" });
        }

        var alreadyMember = await db.BusinessMemberships
            .AnyAsync(m => m.UserId == user.Id && m.BusinessId == invitation.BusinessId && m.IsActive, ct);
        if (alreadyMember)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: "Zaten bu işletmenin ekibindesiniz.",
                extensions: new Dictionary<string, object?> { ["code"] = "already_member" });
        }

        // Davet linki e-postaya geldiği için adres sahipliği kanıtlanmış olur.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        await acceptor.AcceptAsync(invitation.Id, invitation.BusinessId, user, ct);
        await session.SwitchBusinessAsync(user, invitation.BusinessId);
        return TypedResults.Ok(await session.BuildMeAsync(user, invitation.BusinessId, ct));
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult, ValidationProblem>> Register(
        string token,
        AcceptInvitationRegisterRequest request,
        AppDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        SessionService session,
        InvitationAcceptor acceptor,
        TimeProvider clock,
        HttpContext http,
        CancellationToken ct)
    {
        var invitation = await FindAsync(db, token, ct);
        if (invitation is null)
        {
            return NotFound();
        }

        var status = invitation.StatusAt(clock.GetUtcNow().UtcDateTime);
        if (status != InvitationStatus.Pending)
        {
            return NotUsable(status);
        }

        if (await users.FindByEmailAsync(invitation.Email) is not null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "Bu e-posta adresiyle bir hesap var. Giriş yapıp daveti kabul edin.",
                extensions: new Dictionary<string, object?> { ["code"] = "account_exists" });
        }

        var user = new AppUser
        {
            FullName = request.FullName.Trim(),
            UserName = invitation.Email,
            Email = invitation.Email,
            EmailConfirmed = true,
            PhoneNumber = Validation.NormalizePhone(request.Phone),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            LastLoginAt = clock.GetUtcNow().UtcDateTime, // kayıt otomatik oturum açar: ilk giriş sayılır (süper admin "Son giriş")
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return (ValidationProblem)Validation.IdentityProblem(result);
        }

        db.UserConsents.AddRange(AuthEndpoints.Consents(user.Id, clock, http, owner: false));
        await db.SaveChangesAsync(ct);

        await acceptor.AcceptAsync(invitation.Id, invitation.BusinessId, user, ct);
        user.LastBusinessId = invitation.BusinessId;
        await users.UpdateAsync(user);
        await signIn.SignInAsync(user, isPersistent: false);
        return TypedResults.Ok(await session.BuildMeAsync(user, invitation.BusinessId, ct));
    }
}

/// <summary>Daveti, davetin işletmesine ait ayrı bir scope'ta kabul eder: üyelik, bildirimler, aktivite günlüğü.</summary>
public sealed class InvitationAcceptor(IServiceScopeFactory scopes)
{
    public async Task AcceptAsync(Guid invitationId, Guid businessId, AppUser user, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<TenantContext>().Set(businessId, user.Id);

        var db = services.GetRequiredService<AppDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var invitation = await db.Invitations.SingleAsync(i => i.Id == invitationId, ct);
        var businessName = await db.Businesses.Where(b => b.Id == businessId).Select(b => b.Name).FirstAsync(ct);

        // Daha önce ekipten çıkarılmış kullanıcı yeniden katılırsa eski üyelik yeni rolle açılır.
        var membership = await db.BusinessMemberships.FirstOrDefaultAsync(m => m.UserId == user.Id && m.BusinessId == businessId, ct);
        if (membership is null)
        {
            membership = new BusinessMembership { UserId = user.Id, BusinessId = businessId, Role = invitation.Role };
            db.BusinessMemberships.Add(membership);
        }
        else
        {
            membership.Role = invitation.Role;
            membership.IsActive = true;
        }

        invitation.AcceptedAt = now;
        invitation.AcceptedByUserId = user.Id;

        var notifications = services.GetRequiredService<INotificationService>();
        notifications.Notify(invitation.InvitedByUserId, NotificationTypes.InvitationAccepted,
            $"{user.FullName} ekibe katıldı",
            $"{user.FullName}, {invitation.Role.DisplayName()} olarak davetinizi kabul etti.",
            "/panel/ayarlar/ekip");
        notifications.Notify(user.Id, NotificationTypes.JoinedTeam,
            $"{businessName} ekibine hoş geldin",
            $"Rolün: {invitation.Role.DisplayName()}.",
            "/panel");

        services.GetRequiredService<IAuditLogger>().Log(AuditAction.InvitationAccepted, nameof(Invitation), invitation.Id,
            new { email = invitation.Email, role = invitation.Role.ToString() });

        await db.SaveChangesAsync(ct);
    }
}

public sealed class AcceptInvitationRegisterRequestValidator : AbstractValidator<AcceptInvitationRegisterRequest>
{
    public AcceptInvitationRegisterRequestValidator()
    {
        RuleFor(x => x.FullName).ValidFullName();
        RuleFor(x => x.Phone).ValidPhone();
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.TermsAccepted).Equal(true).WithMessage("Devam etmek için Kullanım koşullarını kabul edin.");
    }
}
