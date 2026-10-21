using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Messaging;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Messaging;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Marketing;

/// <summary>
/// SMS kredisi (Ayarlar → SMS). Sahip, Yönetici ve Mali Müşavir bakiye ve hareketleri görür; eşiği Sahip/Yönetici değiştirir.
/// Kredi yüklemeyi bu sprintte sadece platform (PlatformAdmin) yapar; ödeme Sprint 8 (iyzico).
/// </summary>
public static class SmsCreditEndpoints
{
    public static IEndpointRouteBuilder MapSmsCreditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sms-credits").WithTags("Marketing").RequireAuthorization(Policies.ViewMarketing);
        group.MapGet("/", Summary).WithSummary("Bakiye, eşik, son 30 gün harcama");
        group.MapGet("/transactions", Transactions).WithSummary("Kredi hareketleri (yükleme, gönderim, iade; sayfalı)");
        group.MapGet("/transactions.xlsx", Export).RequireAuthorization(Policies.Export).WithSummary("Kredi hareketleri Excel");

        app.MapPut("/api/business/sms-settings", UpdateSettings).WithTags("Marketing").RequireAuthorization(Policies.ManageBusiness)
            .Validate<SmsSettingsRequest>().WithSummary("Düşük bakiye uyarı eşiği (0 = kapalı)");

        var admin = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(Policies.PlatformAdmin);
        admin.MapPost("/businesses/{id:guid}/sms-credits", AdminTopUp).Validate<AdminTopUpRequest>().WithSummary("SMS kredisi yükle");
        return app;
    }

    private static async Task<Ok<SmsCreditSummary>> Summary(CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var businessId = member.RequiredBusinessId;
        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId)
            .Select(b => new { b.SmsBalance, b.SmsLowBalanceThreshold }).FirstAsync(ct);
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-30);
        var spent = await db.SmsCreditTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= since && (t.Kind == SmsCreditKind.Send || t.Kind == SmsCreditKind.Refund))
            .SumAsync(t => (int?)t.Amount, ct) ?? 0;
        var sent = await db.MessageLogs.AsNoTracking().CountAsync(m => m.CreatedAt >= since && m.Billable && m.Status == Domain.Messaging.MessageStatus.Sent, ct);
        var lastTopUp = await db.SmsCreditTransactions.AsNoTracking()
            .Where(t => t.Kind == SmsCreditKind.TopUp || t.Kind == SmsCreditKind.Welcome)
            .MaxAsync(t => (DateTime?)t.CreatedAt, ct);
        var threshold = business.SmsLowBalanceThreshold;
        return TypedResults.Ok(new SmsCreditSummary(business.SmsBalance, threshold, threshold > 0 && business.SmsBalance < threshold,
            -spent, sent, lastTopUp));
    }

    private static IQueryable<SmsCreditTransaction> Filter(AppDbContext db, string? kind, DateOnly? from, DateOnly? to)
    {
        var query = db.SmsCreditTransactions.AsNoTracking();
        if (kind == "in")
        {
            query = query.Where(t => t.Amount > 0);
        }
        else if (kind == "out")
        {
            query = query.Where(t => t.Amount < 0);
        }

        if (from is not null || to is not null)
        {
            var (start, end) = BusinessTime.RangeUtc(from ?? DateOnly.MinValue.AddDays(1), to ?? DateOnly.MaxValue.AddDays(-1));
            query = query.Where(t => t.CreatedAt >= start && t.CreatedAt < end);
        }

        return query;
    }

    private static async Task<List<SmsCreditTransactionItem>> ItemsAsync(AppDbContext db, IQueryable<SmsCreditTransaction> query, CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        var logIds = rows.Where(r => r.MessageLogId != null).Select(r => r.MessageLogId!.Value).Distinct().ToList();
        var logs = await db.MessageLogs.AsNoTracking().Where(m => logIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => new { m.Purpose, m.To }, ct);
        return rows.Select(t =>
        {
            var log = t.MessageLogId is { } id ? logs.GetValueOrDefault(id) : null;
            return new SmsCreditTransactionItem(t.Id, t.CreatedAt, t.Amount, t.Kind, t.BalanceAfter, t.Note,
                log is null ? null : MessageLabels.Purposes[log.Purpose], log?.To);
        }).ToList();
    }

    private static async Task<Ok<SmsCreditTransactionListResponse>> Transactions(
        AppDbContext db, CancellationToken ct, string? kind = null, DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Filter(db, kind, from, to);
        var total = await query.CountAsync(ct);
        var items = await ItemsAsync(db, query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize), ct);
        return TypedResults.Ok(new SmsCreditTransactionListResponse(items, total));
    }

    public static readonly IReadOnlyDictionary<SmsCreditKind, string> KindLabels = new Dictionary<SmsCreditKind, string>
    {
        [SmsCreditKind.TopUp] = "Yükleme",
        [SmsCreditKind.Welcome] = "Hoş geldiniz kredisi",
        [SmsCreditKind.Send] = "Gönderim",
        [SmsCreditKind.Refund] = "İade",
        [SmsCreditKind.Adjustment] = "Düzeltme",
    };

    private static async Task<FileContentHttpResult> Export(
        AppDbContext db, IAuditLogger audit, CancellationToken ct, string? kind = null, DateOnly? from = null, DateOnly? to = null)
    {
        var items = await ItemsAsync(db, Filter(db, kind, from, to).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(50_000), ct);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Kredi hareketleri");
        Xlsx.Header(sheet, 1, "Tarih", "Tür", "Kredi", "Bakiye", "Açıklama", "Alıcı");
        var row = 2;
        foreach (var t in items)
        {
            Xlsx.Row(sheet, row++, BusinessTime.ToLocal(t.CreatedAt), KindLabels[t.Kind], t.Amount, t.BalanceAfter, t.Purpose ?? t.Note ?? "", t.To ?? "");
        }

        Xlsx.Format(sheet, 2, row - 1, dateTimes: [1]);
        audit.Log(AuditAction.SmsCreditsExported, null, null, new { count = items.Count });
        await db.SaveChangesAsync(ct);
        return TypedResults.File(Xlsx.Save(workbook), Xlsx.ContentType, "sms-kredi-hareketleri.xlsx");
    }

    private static async Task<Ok<SmsCreditSummary>> UpdateSettings(
        SmsSettingsRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var business = await db.Businesses.FirstAsync(b => b.Id == member.RequiredBusinessId, ct);
        if (business.SmsLowBalanceThreshold != request.LowBalanceThreshold)
        {
            audit.Log(AuditAction.SmsSettingsChanged, nameof(Business), business.Id,
                new { lowBalanceThreshold = new { from = business.SmsLowBalanceThreshold, to = request.LowBalanceThreshold } });
            business.SmsLowBalanceThreshold = request.LowBalanceThreshold;
            business.SmsLowBalanceNotifiedAt = null;
            await db.SaveChangesAsync(ct);
        }

        return await Summary(member, db, clock, ct);
    }

    // --- Platform ---

    private static async Task<Results<Ok<AdminTopUpResponse>, ProblemHttpResult>> AdminTopUp(
        Guid id, AdminTopUpRequest request, CurrentMember member, AppDbContext db, IServiceScopeFactory scopes, HttpContext http, CancellationToken ct)
    {
        var business = await db.Businesses.AsNoTracking().Where(b => b.Id == id && b.SetupCompletedAt != null)
            .Select(b => new { b.Id, b.Name }).FirstOrDefaultAsync(ct);
        if (business is null)
        {
            return Problems.NotFound("İşletme bulunamadı.");
        }

        // Yükleme hedef işletmenin kiracı bağlamında (hareket o işletmeye yazılır); yapan kişi CreatedBy ve aktivite günlüğünde.
        await using var scope = scopes.TenantScope(business.Id);
        var tenant = scope.ServiceProvider.GetRequiredService<Infrastructure.Tenancy.TenantContext>();
        tenant.SetUser(member.RequiredUserId);
        var credits = scope.ServiceProvider.GetRequiredService<SmsCredits>();
        var balance = await credits.TopUpAsync(request.Amount, SmsCreditKind.TopUp, request.Note.Trim(), ct);

        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<IAuditLogger>().Log(AuditAction.SmsCreditsAdded, nameof(Business), business.Id,
            new { amount = request.Amount, note = request.Note.Trim(), balance, by = "KobiFabrika" });
        Admin.AdminEndpoints.Log(tenantDb, member, http, business.Id, Domain.Platform.PlatformAction.SmsCreditsAdded,
            new { amount = request.Amount, note = request.Note.Trim(), balance }, scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
        await tenantDb.SaveChangesAsync(ct);
        return TypedResults.Ok(new AdminTopUpResponse(business.Id, business.Name, balance));
    }
}
