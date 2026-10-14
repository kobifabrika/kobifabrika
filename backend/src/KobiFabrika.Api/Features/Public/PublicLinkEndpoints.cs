using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Pdf;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Public;

public sealed record PublicStatementRow(DateOnly Date, string Description, decimal Debit, decimal Credit, decimal Balance);

public sealed record PublicStatementResponse(
    string BusinessName, string? LogoUrl, string? BusinessPhone, string? BusinessEmail, string ContactName,
    DateOnly? From, DateOnly To, decimal CarriedBalance, IReadOnlyList<PublicStatementRow> Items,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance, DateTime ExpiresAt);

/// <summary>Status: Open, ClaimedPaid (müşteri "Ödedim" dedi) veya Confirmed (işletme onayladı). Iban boşluksuz; arayüz gruplar.</summary>
public sealed record PublicPaymentResponse(
    string BusinessName, string? LogoUrl, string? BusinessPhone, string ContactName, decimal Amount, string? Note,
    string? Iban, string IbanHolderName, PaymentRequestStatus Status, DateTime ExpiresAt, DateTime? ClaimedAt);

public sealed record ClaimPaymentRequest(string? Note);

/// <summary>
/// İşletmenin müşterisinin açtığı herkese açık sayfaların uçları: /ekstre/[token], /odeme/[token] ve /teklif/[token]. Oturum yok;
/// kiracı token özetinden çözülür ve işlem o işletmenin kiracı bağlamındaki ayrı bir DI scope'unda yapılır.
/// IP başına dakikada sınırlı; yanıtlar arama motorlarına kapalı ve referrer göndermez.
/// </summary>
public static class PublicLinkEndpoints
{
    public const string RateLimit = "public-links";

    public static IEndpointRouteBuilder MapPublicLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public").WithTags("Public").AllowAnonymous().RequireRateLimiting(RateLimit)
            .AddEndpointFilter(PrivateHeaders);

        group.MapGet("/statements/{token}", Statement).WithSummary("Herkese açık ekstre");
        group.MapGet("/statements/{token}/pdf", StatementPdf).WithSummary("Herkese açık ekstre PDF");
        group.MapGet("/payment-requests/{token}", Payment).WithSummary("Tahsilat linki: tutar ve IBAN");
        group.MapPost("/payment-requests/{token}/claim", Claim).Validate<ClaimPaymentRequest>()
            .WithSummary("Müşteri \"Ödedim\" der; Sahip ve Yöneticilere bildirim düşer");
        group.MapPublicQuoteEndpoints();

        return app;
    }

    /// <summary>Herkese açık yanıtlar arama motorlarına kapalı, önbelleğe alınmaz, referrer göndermez.</summary>
    internal static async ValueTask<object?> PrivateHeaders(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var headers = context.HttpContext.Response.Headers;
        headers["X-Robots-Tag"] = "noindex, nofollow";
        headers["Referrer-Policy"] = "no-referrer";
        headers.CacheControl = "no-store";
        return await next(context);
    }

    private static ProblemHttpResult LinkNotFound() => Problems.NotFound("Bağlantı bulunamadı. Linki kontrol edin veya işletmeden yenisini isteyin.");

    private static ProblemHttpResult LinkExpired() =>
        Problems.Gone("link_expired", "Bu bağlantının süresi doldu. İşletmeden yeni bir bağlantı isteyin.");

    private static async Task<StatementShare?> FindShareAsync(AppDbContext db, string token, CancellationToken ct)
    {
        if (!SecureTokens.LooksValid(token))
        {
            return null;
        }

        var hash = SecureTokens.Hash(token);
        return await db.StatementShares.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
    }

    private static async Task<Results<Ok<PublicStatementResponse>, ProblemHttpResult>> Statement(
        string token, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        var share = await FindShareAsync(db, token, ct);
        if (share is null)
        {
            return LinkNotFound();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (share.ExpiresAt <= now)
        {
            return LinkExpired();
        }

        await using var scope = scopes.TenantScope(share.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await tenantDb.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstOrDefaultAsync(c => c.Id == share.ContactId, ct);
        if (contact is null)
        {
            return LinkNotFound();
        }

        var from = StatementShareEndpoints.FromOrNull(share);
        var ledger = await LedgerQueries.BuildAsync(tenantDb, contact, from, share.To, ct);
        var business = await tenantDb.Businesses.AsNoTracking().FirstAsync(b => b.Id == share.BusinessId, ct);
        await tenantDb.StatementShares.Where(s => s.Id == share.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastViewedAt, now), ct);

        return TypedResults.Ok(new PublicStatementResponse(
            business.Name, business.LogoUrl, business.Phone is { } p ? ContactSearchProvider.FormatPhone(p) : null, business.Email,
            contact.DisplayName, from, share.To, ledger.CarriedBalance,
            ledger.Items.Select(i => new PublicStatementRow(i.Date, i.Description, i.Debit, i.Credit, i.Balance)).ToList(),
            ledger.TotalDebit, ledger.TotalCredit, ledger.ClosingBalance, share.ExpiresAt));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> StatementPdf(
        string token, AppDbContext db, IServiceScopeFactory scopes, IFileStorage storage, IPdfRenderer pdf, TimeProvider clock, CancellationToken ct)
    {
        var share = await FindShareAsync(db, token, ct);
        if (share is null)
        {
            return LinkNotFound();
        }

        if (share.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
        {
            return LinkExpired();
        }

        await using var scope = scopes.TenantScope(share.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await tenantDb.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(c => c.Id == share.ContactId, ct);
        var data = await LedgerQueries.StatementAsync(tenantDb, storage, clock, contact, StatementShareEndpoints.FromOrNull(share), share.To, ct);
        return TypedResults.File(pdf.Statement(data), "application/pdf", $"ekstre-{LedgerQueries.FileSafe(data.BusinessName)}.pdf");
    }

    private static async Task<PaymentRequest?> FindPaymentAsync(AppDbContext db, string token, CancellationToken ct)
    {
        if (!SecureTokens.LooksValid(token))
        {
            return null;
        }

        var hash = SecureTokens.Hash(token);
        return await db.PaymentRequests.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().FirstOrDefaultAsync(p => p.TokenHash == hash, ct);
    }

    /// <summary>İptal edilmiş veya süresi dolmuş (ve onaylanmamış) link artık kullanılamaz.</summary>
    private static bool IsClosed(PaymentRequest p, DateTime now) =>
        p.Status == PaymentRequestStatus.Cancelled || (p.Status != PaymentRequestStatus.Confirmed && p.IsExpired(now));

    private static async Task<Results<Ok<PublicPaymentResponse>, ProblemHttpResult>> Payment(
        string token, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        var request = await FindPaymentAsync(db, token, ct);
        if (request is null)
        {
            return LinkNotFound();
        }

        if (IsClosed(request, clock.GetUtcNow().UtcDateTime))
        {
            return LinkExpired();
        }

        await using var scope = scopes.TenantScope(request.BusinessId);
        return TypedResults.Ok(await PaymentResponseAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), request.Id, ct));
    }

    private static async Task<PublicPaymentResponse> PaymentResponseAsync(AppDbContext tenantDb, Guid id, CancellationToken ct)
    {
        var request = await tenantDb.PaymentRequests.AsNoTracking().FirstAsync(p => p.Id == id, ct);
        var business = await tenantDb.Businesses.AsNoTracking().FirstAsync(b => b.Id == request.BusinessId, ct);
        var contactName = await tenantDb.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(c => c.Id == request.ContactId).Select(c => c.DisplayName).FirstAsync(ct);
        return new PublicPaymentResponse(
            business.Name, business.LogoUrl, business.Phone is { } p ? ContactSearchProvider.FormatPhone(p) : null, contactName,
            request.Amount, request.Note, business.Iban, business.IbanHolderName ?? business.Name, request.Status, request.ExpiresAt, request.ClaimedAt);
    }

    private static async Task<Results<Ok<PublicPaymentResponse>, ProblemHttpResult>> Claim(
        string token, ClaimPaymentRequest body, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        var request = await FindPaymentAsync(db, token, ct);
        if (request is null)
        {
            return LinkNotFound();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (IsClosed(request, now))
        {
            return LinkExpired();
        }

        await using var scope = scopes.TenantScope(request.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();

        // Sadece açık link "Ödedim"e geçer; tekrar basmak ikinci bildirim üretmez.
        var changed = await tenantDb.PaymentRequests
            .Where(p => p.Id == request.Id && p.Status == PaymentRequestStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentRequestStatus.ClaimedPaid)
                .SetProperty(p => p.ClaimedAt, now)
                .SetProperty(p => p.ClaimNote, note)
                .SetProperty(p => p.UpdatedAt, now), ct);

        if (changed == 1)
        {
            var contactName = await tenantDb.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete])
                .Where(c => c.Id == request.ContactId).Select(c => c.DisplayName).FirstAsync(ct);
            var managers = await tenantDb.BusinessMemberships
                .Where(m => m.BusinessId == request.BusinessId && m.IsActive && RoleSets.Managers.Contains(m.Role))
                .Select(m => m.UserId)
                .ToListAsync(ct);
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            foreach (var userId in managers)
            {
                notifications.Notify(userId, NotificationTypes.PaymentClaimed, $"{contactName} \"Ödedim\" dedi",
                    $"{ShareLinks.Money(request.Amount)} · Hesaba geçtiyse onaylayın." + (note is null ? "" : $" Not: {note}"),
                    "/panel/cari-kasa?sekme=tahsilat-linkleri");
            }

            await tenantDb.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(await PaymentResponseAsync(tenantDb, request.Id, ct));
    }
}

public sealed class ClaimPaymentRequestValidator : AbstractValidator<ClaimPaymentRequest>
{
    public ClaimPaymentRequestValidator() =>
        RuleFor(x => x.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
}
