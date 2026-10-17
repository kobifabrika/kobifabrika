using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Api.Features.Businesses;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Businesses;
using KobiFabrika.Infrastructure.Identity;
using KobiFabrika.Infrastructure.Locations;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Setup;

public sealed record SetupBusinessRequest(string Name, BusinessSector Sector, string Phone, string City, string District);

public sealed record SetupBusinessDto(
    Guid Id, string Name, string Slug, BusinessSector Sector, string? Phone, string? City, string? District, string? LogoUrl);

public sealed record SetupStateResponse(SetupBusinessDto? Business, IReadOnlyList<WorkingHourDto> WorkingHours);

/// <summary>
/// Kurulum sihirbazı (3 adım): işletme bilgileri, çalışma saatleri, logo. İşletmesi olmayan kullanıcı
/// veya kurulumu bitmemiş işletmenin Owner'ı kullanır; bitince panel açılır.
/// </summary>
public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup").WithTags("Setup").RequireAuthorization(Policies.Setup);

        group.MapGet("/", GetState).WithSummary("Sihirbazın kaldığı yer: işletme bilgileri ve çalışma saatleri");
        group.MapPost("/business", SaveBusiness).Validate<SetupBusinessRequest>()
            .WithSummary("Adım 1: işletmeyi oluştur (kullanıcı Owner olur) veya güncelle");
        group.MapPut("/working-hours", SaveWorkingHours).Validate<WorkingHoursRequest>().WithSummary("Adım 2: çalışma saatleri");
        group.MapPost("/logo", UploadLogo).DisableAntiforgery().WithSummary("Adım 3: logo yükle (isteğe bağlı)");
        group.MapDelete("/logo", DeleteLogo).WithSummary("Adım 3: logoyu kaldır");
        group.MapPost("/complete", Complete).WithSummary("Kurulumu bitir; panel açılır");

        return app;
    }

    private static async Task<Ok<SetupStateResponse>> GetState(CurrentMember member, AppDbContext db, BusinessOperations ops, CancellationToken ct)
    {
        if (!member.IsMember)
        {
            return TypedResults.Ok(new SetupStateResponse(null, WorkingHour.DefaultWeek()
                .Select(d => new WorkingHourDto(d.Day, d.IsClosed, d.OpensAt.ToString("HH:mm"), d.ClosesAt.ToString("HH:mm")))
                .ToList()));
        }

        var business = await db.Businesses.SingleAsync(b => b.Id == member.BusinessId, ct);
        return TypedResults.Ok(new SetupStateResponse(ToDto(business), await ops.GetWorkingHoursAsync(ct)));
    }

    private static SetupBusinessDto ToDto(Business b) =>
        new(b.Id, b.Name, b.Slug, b.Sector, b.Phone, b.City, b.District, b.LogoUrl);

    private static async Task<Ok<MeResponse>> SaveBusiness(
        SetupBusinessRequest request,
        CurrentMember member,
        TenantContext tenant,
        AppDbContext db,
        SlugService slugs,
        UserManager<AppUser> users,
        SessionService session,
        CancellationToken ct)
    {
        var user = (await users.FindByIdAsync(member.RequiredUserId.ToString()))!;
        var name = request.Name.Trim();

        Business business;
        if (member.IsMember)
        {
            // Kullanıcı sihirbazda geri dönüp 1. adımı değiştirdi.
            business = await db.Businesses.SingleAsync(b => b.Id == member.BusinessId, ct);
            if (business.Name != name)
            {
                business.Slug = await slugs.GenerateUniqueAsync(name, business.Id, ct);
            }
        }
        else
        {
            business = new Business { Name = name, Slug = await slugs.GenerateUniqueAsync(name, null, ct) };
            db.Businesses.Add(business);
            db.BusinessMemberships.Add(new BusinessMembership { UserId = user.Id, BusinessId = business.Id, Role = BusinessRole.Owner });

            tenant.Set(business.Id, user.Id);
            db.WorkingHours.AddRange(WorkingHour.DefaultWeek());
            db.CashAccounts.AddRange(CashAccount.DefaultAccounts());
            db.ExpenseCategories.AddRange(ExpenseCategory.Defaults());
            db.LeaveTypes.AddRange(LeaveType.Defaults());
            db.ProductCategories.AddRange(SectorCatalog.SampleCategories(request.Sector).Select(n => new ProductCategory { Name = n }));
        }

        business.Name = name;
        business.SetSector(request.Sector, resetModules: true);
        business.Phone = Validation.NormalizePhone(request.Phone);
        business.City = request.City;
        business.District = request.District;
        business.Email ??= user.Email;
        await slugs.SaveWithUniqueSlugAsync(business, name, ct);

        if (user.LastBusinessId != business.Id)
        {
            await session.SwitchBusinessAsync(user, business.Id);
        }

        return TypedResults.Ok(await session.BuildMeAsync(user, business.Id, ct));
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkingHourDto>>, ProblemHttpResult>> SaveWorkingHours(
        WorkingHoursRequest request, CurrentMember member, BusinessOperations ops, CancellationToken ct)
    {
        if (!member.IsMember)
        {
            return BusinessFirst();
        }

        return TypedResults.Ok(await ops.SaveWorkingHoursAsync(request, logAudit: false, ct));
    }

    private static async Task<IResult> UploadLogo(IFormFile? file, CurrentMember member, AppDbContext db, BusinessOperations ops, CancellationToken ct)
    {
        if (!member.IsMember)
        {
            return BusinessFirst();
        }

        var business = await db.Businesses.SingleAsync(b => b.Id == member.BusinessId, ct);
        return await ops.SaveLogoAsync(business, file, logAudit: false, ct);
    }

    private static async Task<Results<Ok<LogoResponse>, ProblemHttpResult>> DeleteLogo(
        CurrentMember member, AppDbContext db, BusinessOperations ops, CancellationToken ct)
    {
        if (!member.IsMember)
        {
            return BusinessFirst();
        }

        var business = await db.Businesses.SingleAsync(b => b.Id == member.BusinessId, ct);
        return TypedResults.Ok(await ops.DeleteLogoAsync(business, logAudit: false, ct));
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> Complete(
        CurrentMember member,
        AppDbContext db,
        UserManager<AppUser> users,
        SessionService session,
        BusinessOperations ops,
        SmsCredits credits,
        CancellationToken ct)
    {
        if (!member.IsMember)
        {
            return BusinessFirst();
        }

        var business = await db.Businesses.SingleAsync(b => b.Id == member.BusinessId, ct);
        var user = (await users.FindByIdAsync(member.RequiredUserId.ToString()))!;
        if (business.SetupCompletedAt is null)
        {
            // 14 gün deneme kurulum bitince başlar; fiyat sayfasından seçilen paket hatırlanır (karar 2026-10-17).
            business.SetupCompletedAt = ops.Now;
            business.TrialEndsAt ??= ops.Now.AddDays(EffectivePlan.TrialDays);
            business.RequestedPlanCode ??= user.RequestedPlanCode;
            business.RequestedPeriod ??= user.RequestedPeriod;
        }

        await db.SaveChangesAsync(ct);

        // Bir kereye mahsus 50 SMS hoş geldin kredisi (karar 2026-10-15; deneme için).
        await credits.GrantWelcomeAsync(ct);

        return TypedResults.Ok(await session.BuildMeAsync(user, business.Id, ct));
    }

    private static ProblemHttpResult BusinessFirst() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        detail: "Önce işletme bilgilerini kaydedin.",
        extensions: new Dictionary<string, object?> { ["code"] = "business_required" });
}

public sealed class SetupBusinessRequestValidator : AbstractValidator<SetupBusinessRequest>
{
    public SetupBusinessRequestValidator(LocationCatalog locations) =>
        this.AddBasicRules(locations, x => x.Name, x => x.Sector, x => x.Phone, x => x.City, x => x.District);
}
