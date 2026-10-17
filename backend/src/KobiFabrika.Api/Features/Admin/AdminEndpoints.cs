using System.Text.Json;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Auth;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Billing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Platform;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Admin;

/// <summary>
/// Süper admin paneli (sadece PlatformAdmin; karar 2026-10-17). İşletmelerin müşteri, satış ve randevu içeriği görünmez,
/// sadece sayılar; "işletme olarak giriş yap" yok. Her işlem <see cref="PlatformAuditLog"/>'a ve işletmenin kendi aktivite
/// günlüğüne (<see cref="AuditAction.PlatformAccountChanged"/>) yazılır. Kiracı filtresi sadece sayımlarda, açıkça kapatılır.
/// </summary>
public static class AdminEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sayımlarda sadece kiracı filtresi kapatılır (soft delete kalır).</summary>
    private static readonly string[] TenantOnly = [QueryFilters.Tenant];

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(Policies.PlatformAdmin);
        admin.MapGet("/overview", Overview).WithSummary("Özet sayılar ve son kayıtlar");
        admin.MapGet("/businesses", List).WithSummary("İşletmeler (ara: ad, slug, sahip e-postası; filtre: trial, cirak, kalfa, usta, pilot, readonly)");
        admin.MapGet("/businesses/{id:guid}", Detail).WithSummary("İşletme ayrıntısı (sadece sayılar)");
        admin.MapGet("/businesses/{id:guid}/users", Users).WithSummary("İşletmenin kullanıcıları ve rolleri");
        admin.MapPut("/businesses/{id:guid}/plan", ChangePlan).WithSummary("Paketi değiştir (deneme bitince geçerli paket)");
        admin.MapPut("/businesses/{id:guid}/pilot", SetPilot).WithSummary("Pilot işaretle / kaldır");
        admin.MapPut("/businesses/{id:guid}/read-only", SetReadOnly).Validate<AdminReadOnlyRequest>().WithSummary("Salt okunur moda al / çıkar");
        admin.MapPost("/businesses/{id:guid}/trial/extend", ExtendTrial).Validate<AdminTrialExtendRequest>()
            .WithSummary("Denemeyi uzat (bitmişse bugünden itibaren)");
        admin.MapPost("/businesses/{id:guid}/trial/end", EndTrial).WithSummary("Denemeyi şimdi bitir");
        admin.MapGet("/businesses/{id:guid}/notes", Notes).WithSummary("Destek notları");
        admin.MapPost("/businesses/{id:guid}/notes", AddNote).Validate<AdminNoteRequest>().WithSummary("Destek notu ekle");
        admin.MapGet("/businesses/{id:guid}/activity", Activity).WithSummary("Platform işlemleri günlüğü");
        admin.MapGet("/plans", Plans).WithSummary("Paketler, fiyatlar ve limitler");
        admin.MapPut("/plans/{code}", UpdatePlan).Validate<AdminPlanUpdateRequest>().WithSummary("Paket fiyatı, limitleri ve özellikleri");
        return app;
    }

    private static ProblemHttpResult BusinessNotFound() => Problems.NotFound("İşletme bulunamadı.");

    // --- Okuma ---

    private static async Task<Ok<AdminOverview>> Overview(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var all = db.Businesses.AsNoTracking();
        var paid = all.Where(b => !b.IsPilot && (b.TrialEndsAt == null || b.TrialEndsAt <= now));
        var weekAgo = now.AddDays(-7);
        var recent = await ItemsAsync(db, all.OrderByDescending(b => b.CreatedAt).Take(8), now, ct);
        return TypedResults.Ok(new AdminOverview(
            await all.CountAsync(ct),
            await all.CountAsync(b => !b.IsPilot && b.TrialEndsAt > now, ct),
            await all.CountAsync(b => b.IsPilot, ct),
            await paid.CountAsync(b => b.PlanCode == PlanCodes.Cirak, ct),
            await paid.CountAsync(b => b.PlanCode == PlanCodes.Kalfa, ct),
            await paid.CountAsync(b => b.PlanCode == PlanCodes.Usta, ct),
            await all.CountAsync(b => b.IsReadOnly, ct),
            await all.CountAsync(b => b.CreatedAt >= weekAgo, ct),
            await db.Feedbacks.IgnoreQueryFilters(TenantOnly).CountAsync(f => f.Status == FeedbackStatus.New || f.Status == FeedbackStatus.InProgress, ct),
            recent));
    }

    private static async Task<Ok<AdminBusinessList>> List(
        AppDbContext db, TimeProvider clock, CancellationToken ct, string? search = null, string? filter = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.Businesses.AsNoTracking();
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(b => b.Name.Contains(term) || b.Slug.Contains(term)
                || db.BusinessMemberships.Any(m => m.BusinessId == b.Id && m.Role == BusinessRole.Owner
                    && db.Users.Any(u => u.Id == m.UserId && u.Email!.Contains(term))));
        }

        query = filter switch
        {
            "trial" => query.Where(b => !b.IsPilot && b.TrialEndsAt > now),
            "pilot" => query.Where(b => b.IsPilot),
            "readonly" => query.Where(b => b.IsReadOnly),
            PlanCodes.Cirak or PlanCodes.Kalfa or PlanCodes.Usta =>
                query.Where(b => !b.IsPilot && (b.TrialEndsAt == null || b.TrialEndsAt <= now) && b.PlanCode == filter),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        var items = await ItemsAsync(db, query.OrderByDescending(b => b.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize), now, ct);
        return TypedResults.Ok(new AdminBusinessList(items, total));
    }

    /// <summary>Satırlar ve sayılar (kiracı filtresi sayımlarda açıkça kapatılır; içerik okunmaz).</summary>
    private static async Task<List<AdminBusinessItem>> ItemsAsync(AppDbContext db, IQueryable<Business> query, DateTime now, CancellationToken ct)
    {
        var (monthStart, monthEnd) = MonthRange(now);
        var rows = await query.Select(b => new
        {
            Business = b,
            Owner = db.BusinessMemberships.Where(m => m.BusinessId == b.Id && m.Role == BusinessRole.Owner && m.IsActive)
                .OrderBy(m => m.CreatedAt)
                .Select(m => db.Users.Where(u => u.Id == m.UserId).Select(u => new { u.FullName, u.Email }).FirstOrDefault())
                .FirstOrDefault(),
            LastLoginAt = db.BusinessMemberships.Where(m => m.BusinessId == b.Id && m.IsActive)
                .Select(m => db.Users.Where(u => u.Id == m.UserId).Select(u => u.LastLoginAt).FirstOrDefault())
                .Max(),
            Users = db.BusinessMemberships.Count(m => m.BusinessId == b.Id && m.IsActive && m.Role != BusinessRole.Accountant),
            Contacts = db.Contacts.IgnoreQueryFilters(TenantOnly).Count(c => c.BusinessId == b.Id && c.IsCustomer),
            Appointments = db.Appointments.IgnoreQueryFilters(TenantOnly)
                .Count(a => a.BusinessId == b.Id && a.StartAt >= monthStart && a.StartAt < monthEnd && a.Status != AppointmentStatus.Cancelled),
        }).ToListAsync(ct);

        var plans = await db.Plans.AsNoTracking().ToListAsync(ct);
        var usta = plans.Single(p => p.Code == PlanCodes.Usta);
        return rows.Select(r =>
        {
            var b = r.Business;
            var effective = EffectivePlan.Resolve(plans.Single(p => p.Code == b.PlanCode), usta, b.IsPilot, b.TrialEndsAt, b.IsReadOnly, now);
            return new AdminBusinessItem(b.Id, b.Name, b.Slug, b.Sector, b.City, b.CreatedAt, b.SetupCompletedAt != null,
                effective.Status, b.PlanCode, effective.PlanName, b.TrialEndsAt, b.IsReadOnly, r.Owner?.FullName, r.Owner?.Email,
                r.LastLoginAt, r.Users, r.Contacts, r.Appointments, b.SmsBalance);
        }).ToList();
    }

    private static (DateTime Start, DateTime End) MonthRange(DateTime nowUtc)
    {
        var today = BusinessTime.LocalDay(nowUtc);
        var first = new DateOnly(today.Year, today.Month, 1);
        return BusinessTime.RangeUtc(first, first.AddMonths(1).AddDays(-1));
    }

    private static async Task<Results<Ok<AdminBusinessDetail>, ProblemHttpResult>> Detail(
        Guid id, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var summary = (await ItemsAsync(db, db.Businesses.AsNoTracking().Where(b => b.Id == id), now, ct)).SingleOrDefault();
        if (summary is null)
        {
            return BusinessNotFound();
        }

        var b = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return TypedResults.Ok(new AdminBusinessDetail(summary, b.Phone, b.Email, b.District, b.ReadOnlyReason, b.RequestedPlanCode, b.RequestedPeriod,
            await db.Appointments.IgnoreQueryFilters(TenantOnly).CountAsync(a => a.BusinessId == id, ct),
            await db.Sales.IgnoreQueryFilters(TenantOnly).CountAsync(s => s.BusinessId == id, ct),
            await db.StaffMembers.IgnoreQueryFilters(TenantOnly).CountAsync(s => s.BusinessId == id, ct),
            b.BookingEnabled,
            await db.Feedbacks.IgnoreQueryFilters(TenantOnly)
                .CountAsync(f => f.BusinessId == id && (f.Status == FeedbackStatus.New || f.Status == FeedbackStatus.InProgress), ct)));
    }

    private static async Task<Results<Ok<IReadOnlyList<AdminUserItem>>, ProblemHttpResult>> Users(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Businesses.AnyAsync(b => b.Id == id, ct))
        {
            return BusinessNotFound();
        }

        var users = await db.BusinessMemberships.AsNoTracking().Where(m => m.BusinessId == id)
            .OrderByDescending(m => m.IsActive).ThenBy(m => m.CreatedAt)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new AdminUserItem(u.Id, u.FullName, u.Email!, u.EmailConfirmed, u.PhoneNumber,
                m.Role, m.IsActive, m.CreatedAt, u.LastLoginAt))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AdminUserItem>>(users);
    }

    private static async Task<Results<Ok<IReadOnlyList<AdminNote>>, ProblemHttpResult>> Notes(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Businesses.AnyAsync(b => b.Id == id, ct))
        {
            return BusinessNotFound();
        }

        var notes = await db.SupportNotes.AsNoTracking().Where(n => n.BusinessId == id).OrderByDescending(n => n.CreatedAt)
            .Select(n => new AdminNote(n.Id, n.Body, db.Users.Where(u => u.Id == n.AuthorUserId).Select(u => u.FullName).FirstOrDefault() ?? "—",
                n.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AdminNote>>(notes);
    }

    private static async Task<Results<Ok<IReadOnlyList<AdminActivityItem>>, ProblemHttpResult>> Activity(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Businesses.AnyAsync(b => b.Id == id, ct))
        {
            return BusinessNotFound();
        }

        var items = await db.PlatformAuditLogs.AsNoTracking().Where(l => l.BusinessId == id).OrderByDescending(l => l.At).Take(200)
            .Select(l => new AdminActivityItem(l.Id, l.Action, l.Details,
                db.Users.Where(u => u.Id == l.AdminUserId).Select(u => u.FullName).FirstOrDefault() ?? "—", l.At))
            .ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AdminActivityItem>>(items);
    }

    // --- Yazma ---

    /// <summary>
    /// İşletme üzerinde süper admin işlemi: işletmenin kiracı bağlamındaki scope'ta değişiklik, platform günlüğü ve işletmenin
    /// aktivite günlüğü tek SaveChanges'te.
    /// </summary>
    private static async Task<Results<Ok<AdminBusinessDetail>, ProblemHttpResult>> ChangeAsync(
        Guid id, CurrentMember member, IServiceScopeFactory scopes, HttpContext http, PlatformAction action,
        Func<Business, DateTime, object?> apply, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Businesses.AnyAsync(b => b.Id == id, ct))
        {
            return BusinessNotFound();
        }

        await using (var scope = scopes.TenantScope(id))
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(member.RequiredUserId);
            var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var business = await tenantDb.Businesses.SingleAsync(b => b.Id == id, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var details = apply(business, now);
            if (details is not null)
            {
                Log(tenantDb, member, http, id, action, details, now);
                scope.ServiceProvider.GetRequiredService<IAuditLogger>().Log(AuditAction.PlatformAccountChanged, nameof(Business), id,
                    new { kind = action.ToString(), details, by = "KobiFabrika destek" });
                await tenantDb.SaveChangesAsync(ct);
            }
        }

        return await Detail(id, db, clock, ct);
    }

    internal static void Log(AppDbContext db, CurrentMember member, HttpContext http, Guid? businessId, PlatformAction action, object details, DateTime now) =>
        db.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            AdminUserId = member.RequiredUserId,
            BusinessId = businessId,
            Action = action,
            Details = JsonSerializer.Serialize(details, Json),
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            At = now,
        });

    private static async Task<Results<Ok<AdminBusinessDetail>, ProblemHttpResult>> ChangePlan(
        Guid id, AdminPlanRequest request, CurrentMember member, IServiceScopeFactory scopes, HttpContext http, AppDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        if (!PlanCodes.IsValid(request.PlanCode))
        {
            return Problems.BadRequest("invalid_plan", "Paket bulunamadı.");
        }

        return await ChangeAsync(id, member, scopes, http, PlatformAction.PlanChanged, (b, _) =>
        {
            if (b.PlanCode == request.PlanCode)
            {
                return null;
            }

            var before = b.PlanCode;
            b.PlanCode = request.PlanCode;
            return new { before, after = request.PlanCode };
        }, db, clock, ct);
    }

    private static Task<Results<Ok<AdminBusinessDetail>, ProblemHttpResult>> SetPilot(
        Guid id, AdminPilotRequest request, CurrentMember member, IServiceScopeFactory scopes, HttpContext http, AppDbContext db,
        TimeProvider clock, CancellationToken ct) =>
        ChangeAsync(id, member, scopes, http, PlatformAction.PilotChanged, (b, _) =>
        {
            if (b.IsPilot == request.IsPilot)
            {
                return null;
            }

            b.IsPilot = request.IsPilot;
            return new { isPilot = request.IsPilot };
        }, db, clock, ct);

    private static Task<Results<Ok<AdminBusinessDetail>, ProblemHttpResult>> SetReadOnly(
        Guid id, AdminReadOnlyRequest request, CurrentMember member, IServiceScopeFactory scopes, HttpContext http, AppDbContext db,
        TimeProvider clock, CancellationToken ct) =>
        ChangeAsync(id, member, scopes, http, PlatformAction.ReadOnlyChanged, (b, _) =>
        {
            var reason = request.IsReadOnly ? request.Reason?.Trim() : null;
            if (b.IsReadOnly == request.IsReadOnly && b.ReadOnlyReason == reason)
            {
                return null;
            }

            b.IsReadOnly = request.IsReadOnly;
            b.ReadOnlyReason = string.IsNullOrEmpty(reason) ? null : reason;
            return new { isReadOnly = request.IsReadOnly, reason = b.ReadOnlyReason };
        }, db, clock, ct);

    private static Task<Results<Ok<AdminBusinessDetail>, ProblemHttpResult>> ExtendTrial(
        Guid id, AdminTrialExtendRequest request, CurrentMember member, IServiceScopeFactory scopes, HttpContext http, AppDbContext db,
        TimeProvider clock, CancellationToken ct) =>
        ChangeAsync(id, member, scopes, http, PlatformAction.TrialExtended, (b, now) =>
        {
            var before = b.TrialEndsAt;
            var start = b.TrialEndsAt is { } end && end > now ? end : now;
            b.TrialEndsAt = start.AddDays(request.Days);
            return new { before, after = b.TrialEndsAt, days = request.Days };
        }, db, clock, ct);

    private static Task<Results<Ok<AdminBusinessDetail>, ProblemHttpResult>> EndTrial(
        Guid id, CurrentMember member, IServiceScopeFactory scopes, HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        ChangeAsync(id, member, scopes, http, PlatformAction.TrialEnded, (b, now) =>
        {
            if (b.TrialEndsAt is not { } end || end <= now)
            {
                return null;
            }

            b.TrialEndsAt = now;
            return new { before = end, after = now };
        }, db, clock, ct);

    private static async Task<Results<Created<AdminNote>, ProblemHttpResult>> AddNote(
        Guid id, AdminNoteRequest request, CurrentMember member, HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Businesses.AnyAsync(b => b.Id == id, ct))
        {
            return BusinessNotFound();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var note = new SupportNote { BusinessId = id, AuthorUserId = member.RequiredUserId, Body = request.Body.Trim(), CreatedAt = now };
        db.SupportNotes.Add(note);
        Log(db, member, http, id, PlatformAction.NoteAdded, new { noteId = note.Id }, now);
        await db.SaveChangesAsync(ct);
        var author = await db.Users.Where(u => u.Id == member.RequiredUserId).Select(u => u.FullName).FirstAsync(ct);
        return TypedResults.Created($"/api/admin/businesses/{id}/notes", new AdminNote(note.Id, note.Body, author, note.CreatedAt));
    }

    private static async Task<Ok<IReadOnlyList<AdminPlanItem>>> Plans(AppDbContext db, CancellationToken ct)
    {
        var plans = await db.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<AdminPlanItem>>(plans.Select(AdminPlans.ToItem).ToList());
    }

    private static async Task<Results<Ok<AdminPlanItem>, ProblemHttpResult>> UpdatePlan(
        string code, AdminPlanUpdateRequest request, CurrentMember member, HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plan is null)
        {
            return Problems.NotFound("Paket bulunamadı.");
        }

        var before = AdminPlans.ToItem(plan);
        var now = clock.GetUtcNow().UtcDateTime;
        plan.MonthlyPrice = request.MonthlyPrice;
        plan.YearlyPrice = request.YearlyPrice;
        plan.MaxUsers = request.MaxUsers;
        plan.MaxContacts = request.MaxContacts;
        plan.MaxAppointmentsPerMonth = request.MaxAppointmentsPerMonth;
        plan.Features = request.Features.Aggregate(PlanFeatures.None, (acc, key) => acc | AdminPlans.FeatureFromKey(key)!.Value);
        plan.UpdatedAt = now;
        plan.UpdatedBy = member.RequiredUserId;
        Log(db, member, http, null, PlatformAction.PlanSettingsChanged, new { code, before, after = request }, now);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AdminPlans.ToItem(plan));
    }
}

public static class AdminPlans
{
    public static AdminPlanItem ToItem(Plan p) => new(p.Code, p.Name, p.MonthlyPrice, p.YearlyPrice, p.MaxUsers, p.MaxContacts,
        p.MaxAppointmentsPerMonth, MePlan.FeatureKeys(p.Features), p.UpdatedAt);

    public static PlanFeatures? FeatureFromKey(string key) =>
        Enum.GetValues<PlanFeatures>().Where(f => f is not PlanFeatures.None and not PlanFeatures.All)
            .Cast<PlanFeatures?>()
            .FirstOrDefault(f => JsonNamingPolicy.CamelCase.ConvertName(f!.Value.ToString()) == key);
}
