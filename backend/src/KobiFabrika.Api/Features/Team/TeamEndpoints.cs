using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Infrastructure.Businesses;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Team;

public sealed record MemberResponse(Guid MembershipId, Guid UserId, string FullName, string Email, BusinessRole Role, bool IsYou, DateTime JoinedAt);

public sealed record InvitationResponse(Guid Id, string Email, BusinessRole Role, InvitationStatus Status, DateTime ExpiresAt, DateTime CreatedAt);

public sealed record InviteRequest(string Email, BusinessRole Role);

public sealed record ChangeRoleRequest(BusinessRole Role);

/// <summary>
/// Ekip yönetimi (/panel/ayarlar/ekip). Owner ve Manager kullanır. Kurallar (karar günlüğü, Sprint 1):
/// Owner rolünü sadece Owner verir/alır; kimse kendi rolünü değiştiremez veya kendini çıkaramaz;
/// son aktif Owner düşürülemez. Davet göndermek doğrulanmış e-posta ister.
/// </summary>
public static class TeamEndpoints
{
    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/team").WithTags("Team").RequireAuthorization(Policies.ManageTeam);

        group.MapGet("/members", Members).WithSummary("Aktif ekip üyeleri");
        group.MapPut("/members/{membershipId:guid}/role", ChangeRole).WithSummary("Üyenin rolünü değiştir");
        group.MapDelete("/members/{membershipId:guid}", RemoveMember).WithSummary("Üyeyi ekipten çıkar");
        group.MapGet("/invitations", Invitations).WithSummary("Bekleyen ve süresi dolmuş davetler");
        group.MapPost("/invitations", Invite).RequireAuthorization(Policies.VerifiedEmail).Validate<InviteRequest>()
            .WithSummary("E-posta ile davet gönder (7 gün geçerli, tek kullanımlık)");
        group.MapPost("/invitations/{id:guid}/resend", Resend).RequireAuthorization(Policies.VerifiedEmail)
            .WithSummary("Daveti yeni bağlantıyla yeniden gönder");
        group.MapDelete("/invitations/{id:guid}", Revoke).WithSummary("Daveti iptal et");

        return app;
    }

    private static async Task<Ok<List<MemberResponse>>> Members(CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        var businessId = member.RequiredBusinessId;
        var members = await db.BusinessMemberships
            .Where(m => m.BusinessId == businessId && m.IsActive)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .OrderBy(x => x.m.Role).ThenBy(x => x.u.FullName)
            .Select(x => new MemberResponse(x.m.Id, x.u.Id, x.u.FullName, x.u.Email!, x.m.Role, x.u.Id == member.UserId, x.m.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(members);
    }

    /// <summary>Hedef üye ve rol değişikliği için ortak kurallar. Hata yoksa null döner.</summary>
    private static async Task<IResult?> CheckTargetAsync(
        BusinessMembership? target, BusinessRole? newRole, CurrentMember member, AppDbContext db, CancellationToken ct)
    {
        if (target is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Ekip üyesi bulunamadı.");
        }

        if (target.UserId == member.UserId)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                detail: newRole is null ? "Kendinizi ekipten çıkaramazsınız." : "Kendi rolünüzü değiştiremezsiniz.",
                extensions: new Dictionary<string, object?> { ["code"] = "self_change" });
        }

        if (member.Role != BusinessRole.Owner && (target.Role == BusinessRole.Owner || newRole == BusinessRole.Owner))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                detail: "Sahip rolünü sadece işletme sahibi verebilir veya değiştirebilir.",
                extensions: new Dictionary<string, object?> { ["code"] = "owner_only" });
        }

        if (target.Role == BusinessRole.Owner && newRole != BusinessRole.Owner)
        {
            var owners = await db.BusinessMemberships.CountAsync(
                m => m.BusinessId == target.BusinessId && m.IsActive && m.Role == BusinessRole.Owner, ct);
            if (owners <= 1)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                    detail: "İşletmenin en az bir sahibi olmalı.",
                    extensions: new Dictionary<string, object?> { ["code"] = "last_owner" });
            }
        }

        return null;
    }

    // BusinessMemberships kiracı filtresi dışında; her sorgu aktif işletmeyle açıkça süzülür.
    private static Task<BusinessMembership?> FindMemberAsync(Guid membershipId, CurrentMember member, AppDbContext db, CancellationToken ct) =>
        db.BusinessMemberships.Include(m => m.Business)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.BusinessId == member.RequiredBusinessId && m.IsActive, ct);

    private static async Task<IResult> ChangeRole(
        Guid membershipId,
        ChangeRoleRequest request,
        CurrentMember member,
        AppDbContext db,
        IAuditLogger audit,
        INotificationService notifications,
        CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Role))
        {
            return Validation.FieldProblem("role", "Rol seçin.");
        }

        var target = await FindMemberAsync(membershipId, member, db, ct);
        if (await CheckTargetAsync(target, request.Role, member, db, ct) is { } problem)
        {
            return problem;
        }

        if (target!.Role != request.Role)
        {
            audit.Log(AuditAction.RoleChanged, nameof(BusinessMembership), target.Id,
                new { userId = target.UserId, from = target.Role.ToString(), to = request.Role.ToString() });
            notifications.Notify(target.UserId, NotificationTypes.RoleChanged, "Rolün değişti",
                $"{target.Business!.Name} işletmesindeki rolün artık {request.Role.DisplayName()}.", "/panel");
            target.Role = request.Role;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveMember(
        Guid membershipId, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var target = await FindMemberAsync(membershipId, member, db, ct);
        if (await CheckTargetAsync(target, null, member, db, ct) is { } problem)
        {
            return problem;
        }

        target!.IsActive = false;
        audit.Log(AuditAction.MemberRemoved, nameof(BusinessMembership), target.Id,
            new { userId = target.UserId, role = target.Role.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static InvitationResponse ToResponse(Invitation i, DateTime now) =>
        new(i.Id, i.Email, i.Role, i.StatusAt(now), i.ExpiresAt, i.CreatedAt);

    private static async Task<Ok<List<InvitationResponse>>> Invitations(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invitations = await db.Invitations
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(invitations.Select(i => ToResponse(i, now)).ToList());
    }

    private static async Task<IResult> Invite(
        InviteRequest request,
        CurrentMember member,
        AppDbContext db,
        IPlanAccess plans,
        IAuditLogger audit,
        InvitationMailer mailer,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request.Role == BusinessRole.Owner && member.Role != BusinessRole.Owner)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                detail: "Sahip rolüyle sadece işletme sahibi davet edebilir.",
                extensions: new Dictionary<string, object?> { ["code"] = "owner_only" });
        }

        var email = Validation.NormalizeEmail(request.Email);
        var businessId = member.RequiredBusinessId;
        var now = clock.GetUtcNow().UtcDateTime;

        var normalizedEmail = email.ToUpperInvariant();
        var alreadyMember = await db.BusinessMemberships
            .AnyAsync(m => m.BusinessId == businessId && m.IsActive && db.Users.Any(u => u.Id == m.UserId && u.NormalizedEmail == normalizedEmail), ct);
        if (alreadyMember)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: "Bu kişi zaten ekipte.",
                extensions: new Dictionary<string, object?> { ["code"] = "already_member" });
        }

        if (await db.Invitations.AnyAsync(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, ct))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "Bu adrese bekleyen bir davet var; listeden yeniden gönderebilirsiniz.",
                extensions: new Dictionary<string, object?> { ["code"] = "already_invited" });
        }

        if (await plans.CheckUserAsync(request.Role, ct) is { Allowed: false } limit)
        {
            return Problems.PlanLimit(limit);
        }

        var (token, hash) = InvitationTokens.Create();
        var invitation = new Invitation
        {
            Email = email,
            Role = request.Role,
            TokenHash = hash,
            ExpiresAt = now + Invitation.Lifetime,
            InvitedByUserId = member.RequiredUserId,
        };
        db.Invitations.Add(invitation);
        audit.Log(AuditAction.InvitationSent, nameof(Invitation), invitation.Id, new { email, role = request.Role.ToString() });
        await db.SaveChangesAsync(ct);

        await mailer.SendAsync(invitation, token, ct);
        return TypedResults.Created($"/api/team/invitations/{invitation.Id}", ToResponse(invitation, now));
    }

    private static async Task<IResult> Resend(
        Guid id, CurrentMember member, AppDbContext db, IAuditLogger audit, InvitationMailer mailer, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.AcceptedAt == null && i.RevokedAt == null, ct);
        if (invitation is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Davet bulunamadı.");
        }

        if (invitation.Role == BusinessRole.Owner && member.Role != BusinessRole.Owner)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                detail: "Sahip rolündeki daveti sadece işletme sahibi yeniden gönderebilir.");
        }

        var (token, hash) = InvitationTokens.Create();
        invitation.TokenHash = hash;
        invitation.ExpiresAt = now + Invitation.Lifetime;
        audit.Log(AuditAction.InvitationResent, nameof(Invitation), invitation.Id, new { email = invitation.Email });
        await db.SaveChangesAsync(ct);

        await mailer.SendAsync(invitation, token, ct);
        return TypedResults.Ok(ToResponse(invitation, now));
    }

    private static async Task<IResult> Revoke(
        Guid id, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.AcceptedAt == null && i.RevokedAt == null, ct);
        if (invitation is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Davet bulunamadı.");
        }

        if (invitation.Role == BusinessRole.Owner && member.Role != BusinessRole.Owner)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                detail: "Sahip rolündeki daveti sadece işletme sahibi iptal edebilir.");
        }

        invitation.RevokedAt = clock.GetUtcNow().UtcDateTime;
        audit.Log(AuditAction.InvitationRevoked, nameof(Invitation), invitation.Id, new { email = invitation.Email });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

/// <summary>Davet e-postası: işletme adı, davet eden ve rolle birlikte /davet/{token} linki.</summary>
public sealed class InvitationMailer(AppDbContext db, EmailDispatch dispatch, IOptions<AppOptions> app)
{
    public async Task SendAsync(Invitation invitation, string token, CancellationToken ct)
    {
        var businessName = await db.Businesses.Where(b => b.Id == invitation.BusinessId).Select(b => b.Name).FirstAsync(ct);
        var inviter = await db.Users.Where(u => u.Id == invitation.InvitedByUserId).Select(u => u.FullName).FirstAsync(ct);
        var link = app.Value.Link($"/davet/{token}");
        await dispatch.TrySendAsync(
            EmailTemplates.Invitation(invitation.Email, businessName, inviter, invitation.Role.DisplayName(), link), ct);
    }
}

public sealed class InviteRequestValidator : AbstractValidator<InviteRequest>
{
    public InviteRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Role).IsInEnum().WithMessage("Rol seçin.");
    }
}
