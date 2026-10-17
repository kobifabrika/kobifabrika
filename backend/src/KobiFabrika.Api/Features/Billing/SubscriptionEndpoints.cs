using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Admin;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Billing;

/// <summary>Kullanım ve limit: null limit = sınırsız. AppointmentsThisMonth: bu İstanbul ayı, iptaller hariç.</summary>
public sealed record SubscriptionUsage(int Users, int? MaxUsers, int Contacts, int? MaxContacts, int AppointmentsThisMonth, int? MaxAppointmentsPerMonth);

public sealed record SubscriptionResponse(MePlan Plan, SubscriptionUsage Usage, IReadOnlyList<AdminPlanItem> Plans, bool OnlinePaymentAvailable);

public sealed record RequestedPlanRequest(string Plan, string Period);

public sealed class RequestedPlanRequestValidator : AbstractValidator<RequestedPlanRequest>
{
    public RequestedPlanRequestValidator()
    {
        RuleFor(x => x.Plan).Must(PlanCodes.IsValid).WithMessage("Paket bulunamadı.");
        RuleFor(x => x.Period).Must(PlanPeriods.IsValid).WithMessage("Dönem aylık veya yıllık olmalı.");
    }
}

/// <summary>
/// Paketler ve abonelik (ödemesiz; karar 2026-10-17). Fiyat sayfası paketleri herkese açık okur. Abonelik ekranını Sahip görür
/// (Yönetici abonelik göremez, docs/01). "Paket seç" ödeme almaz: tercih kaydedilir, değişikliği destek yapar.
/// Online ödeme (iyzico) şirket kurulunca: <see cref="Infrastructure.Billing.IPaymentProvider"/> kapalı.
/// </summary>
public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plans", PublicPlans).WithTags("Billing").AllowAnonymous().WithSummary("Paketler, fiyatlar (KDV hariç) ve limitler");

        var group = app.MapGroup("/api/subscription").WithTags("Billing").RequireAuthorization(Policies.OwnerOnly);
        group.MapGet("/", Get).WithSummary("Paket, deneme bitişi, kullanım ve limitler, paket karşılaştırması");
        group.MapPut("/requested-plan", Request).Validate<RequestedPlanRequest>()
            .WithSummary("Paket tercihi (ödeme alınmaz; destek değiştirir)");
        return app;
    }

    private static async Task<Ok<IReadOnlyList<AdminPlanItem>>> PublicPlans(AppDbContext db, CancellationToken ct)
    {
        var plans = await db.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AdminPlanItem>>(plans.Select(AdminPlans.ToItem).ToList());
    }

    private static async Task<Ok<SubscriptionResponse>> Get(
        CurrentMember member, AppDbContext db, IPlanAccess access, TimeProvider clock, Infrastructure.Billing.IPaymentProvider payments,
        CancellationToken ct)
    {
        var plan = await access.GetAsync(ct);
        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == member.RequiredBusinessId)
            .Select(b => new { b.RequestedPlanCode, b.RequestedPeriod }).FirstAsync(ct);
        var usage = new SubscriptionUsage(
            await access.CountBillableUsersAsync(ct), plan.MaxUsers,
            await access.CountContactsAsync(ct), plan.MaxContacts,
            await access.CountAppointmentsInMonthAsync(BusinessTime.Today(clock), null, ct), plan.MaxAppointmentsPerMonth);
        var plans = await db.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        var me = new MePlan(plan.Status, plan.PlanCode, plan.PlanName, plan.TrialEndsAt, plan.IsReadOnly, MePlan.FeatureKeys(plan.Features),
            plan.MaxUsers, plan.MaxContacts, plan.MaxAppointmentsPerMonth, business.RequestedPlanCode, business.RequestedPeriod);
        return TypedResults.Ok(new SubscriptionResponse(me, usage, plans.Select(AdminPlans.ToItem).ToList(), payments.IsEnabled));
    }

    private static async Task<Ok<SubscriptionResponse>> Request(
        RequestedPlanRequest request, CurrentMember member, AppDbContext db, IPlanAccess access, IAuditLogger audit, TimeProvider clock,
        Infrastructure.Billing.IPaymentProvider payments, CancellationToken ct)
    {
        var business = await db.Businesses.SingleAsync(b => b.Id == member.RequiredBusinessId, ct);
        if (business.RequestedPlanCode != request.Plan || business.RequestedPeriod != request.Period)
        {
            audit.Log(AuditAction.BusinessUpdated, nameof(Business), business.Id,
                new { requestedPlan = new { before = business.RequestedPlanCode, after = request.Plan }, period = request.Period });
            business.RequestedPlanCode = request.Plan;
            business.RequestedPeriod = request.Period;
            await db.SaveChangesAsync(ct);
        }

        return await Get(member, db, access, clock, payments, ct);
    }
}
