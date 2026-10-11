using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Quotes;
using KobiFabrika.Api.Features.Sales;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Pdf;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Public;

/// <summary>CanRespond: müşteri hâlâ onaylayabilir/reddedebilir (gönderilmiş ve geçerli).</summary>
public sealed record PublicQuoteResponse(
    string BusinessName, string? LogoUrl, string? BusinessPhone, string? BusinessEmail, string? BusinessAddress, string ContactName,
    string No, DateOnly Date, DateOnly ValidUntil, QuoteStatus Status, bool CanRespond, IReadOnlyList<DocumentLineResponse> Lines,
    decimal SubTotal, decimal LineDiscountTotal, decimal DiscountTotal, decimal VatTotal, decimal Total, IReadOnlyList<VatBreakdown> Vat,
    string? Notes, DateTime? RespondedAt, string? ResponseNote);

/// <summary>
/// Herkese açık teklif sayfası (/teklif/[token]) uçları: görüntüle, PDF, onayla, reddet. Ekstre ve tahsilat linkiyle aynı güvenlik:
/// token özetiyle bulunur, kiracı token'dan çözülür, geçerlilik gününün sonunda 410. Onay/ret koşullu UPDATE ile bir kez yapılır;
/// Sahip, Yönetici ve teklifi hazırlayana bildirim düşer.
/// </summary>
public static class PublicQuoteEndpoints
{
    public static RouteGroupBuilder MapPublicQuoteEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/quotes/{token}", Get).WithSummary("Herkese açık teklif");
        group.MapGet("/quotes/{token}/pdf", Pdf).WithSummary("Herkese açık teklif PDF");
        group.MapPost("/quotes/{token}/accept", (string token, RespondQuoteRequest body, AppDbContext db, IServiceScopeFactory scopes,
                TimeProvider clock, CancellationToken ct) => RespondAsync(token, QuoteStatus.Accepted, body, db, scopes, clock, ct))
            .Validate<RespondQuoteRequest>().WithSummary("Müşteri teklifi onaylar");
        group.MapPost("/quotes/{token}/reject", (string token, RespondQuoteRequest body, AppDbContext db, IServiceScopeFactory scopes,
                TimeProvider clock, CancellationToken ct) => RespondAsync(token, QuoteStatus.Rejected, body, db, scopes, clock, ct))
            .Validate<RespondQuoteRequest>().WithSummary("Müşteri teklifi reddeder (isteğe bağlı not)");
        return group;
    }

    private static ProblemHttpResult LinkNotFound() => Problems.NotFound("Bağlantı bulunamadı. Linki kontrol edin veya işletmeden yenisini isteyin.");

    private static ProblemHttpResult LinkExpired() =>
        Problems.Gone("link_expired", "Bu teklifin süresi doldu. İşletmeden güncel teklif isteyin.");

    /// <summary>Token'ın teklifini bulur (silinmiş teklif bulunmaz); süresi dolmuşsa 410.</summary>
    private static async Task<(Quote? Quote, ProblemHttpResult? Problem)> FindAsync(AppDbContext db, string token, TimeProvider clock, CancellationToken ct)
    {
        if (!SecureTokens.LooksValid(token))
        {
            return (null, LinkNotFound());
        }

        var hash = SecureTokens.Hash(token);
        var quote = await db.Quotes.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().FirstOrDefaultAsync(q => q.TokenHash == hash, ct);
        if (quote is null)
        {
            return (null, LinkNotFound());
        }

        return quote.ValidUntil < BusinessTime.Today(clock) ? (null, LinkExpired()) : (quote, null);
    }

    private static async Task<PublicQuoteResponse> ResponseAsync(AppDbContext tenantDb, Guid quoteId, TimeProvider clock, CancellationToken ct)
    {
        var quote = (await QuoteEndpoints.LoadAsync(tenantDb, null, quoteId, clock, ct))!;
        var business = await tenantDb.Businesses.AsNoTracking().FirstAsync(b => b.Id == tenantDb.Quotes.Where(q => q.Id == quoteId).Select(q => q.BusinessId).First(), ct);
        var address = string.Join(", ", new[] { business.Address, business.District, business.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new PublicQuoteResponse(
            business.Name, business.LogoUrl, business.Phone is { } p ? ContactSearchProvider.FormatPhone(p) : null, business.Email,
            address.Length > 0 ? address : null, quote.ContactName, quote.No, quote.Date, quote.ValidUntil, quote.Status,
            quote.Status == QuoteStatus.Sent && !quote.IsExpired, quote.Lines, quote.SubTotal, quote.LineDiscountTotal, quote.DiscountTotal,
            quote.VatTotal, quote.Total, quote.Vat, quote.Notes, quote.RespondedAt, quote.ResponseNote);
    }

    private static async Task<Results<Ok<PublicQuoteResponse>, ProblemHttpResult>> Get(
        string token, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        var (quote, problem) = await FindAsync(db, token, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var scope = scopes.TenantScope(quote!.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        await tenantDb.Quotes.Where(q => q.Id == quote.Id).ExecuteUpdateAsync(s => s.SetProperty(q => q.LastViewedAt, now), ct);
        return TypedResults.Ok(await ResponseAsync(tenantDb, quote.Id, clock, ct));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Pdf(
        string token, AppDbContext db, IServiceScopeFactory scopes, IFileStorage storage, IPdfRenderer pdf, TimeProvider clock, CancellationToken ct)
    {
        var (quote, problem) = await FindAsync(db, token, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var scope = scopes.TenantScope(quote!.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var response = (await QuoteEndpoints.LoadAsync(tenantDb, null, quote.Id, clock, ct))!;
        var data = await QuoteEndpoints.DocumentAsync(tenantDb, storage, response, quote.BusinessId, clock, ct);
        return TypedResults.File(pdf.Quote(data), "application/pdf", QuoteEndpoints.FileName(quote.No));
    }

    private static async Task<Results<Ok<PublicQuoteResponse>, ProblemHttpResult>> RespondAsync(
        string token, QuoteStatus status, RespondQuoteRequest body, AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        var (quote, problem) = await FindAsync(db, token, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var scope = scopes.TenantScope(quote!.BusinessId);
        var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();

        // Sadece gönderilmiş teklif cevaplanır; tekrar basmak ikinci bildirim üretmez.
        var changed = await tenantDb.Quotes.Where(q => q.Id == quote.Id && q.Status == QuoteStatus.Sent)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.Status, status)
                .SetProperty(q => q.RespondedAt, now)
                .SetProperty(q => q.RespondedVia, QuoteResponseChannel.Link)
                .SetProperty(q => q.ResponseNote, note)
                .SetProperty(q => q.UpdatedAt, now), ct);

        if (changed == 1)
        {
            var contactName = await tenantDb.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete])
                .Where(c => c.Id == quote.ContactId).Select(c => c.DisplayName).FirstAsync(ct);
            var recipients = await tenantDb.BusinessMemberships
                .Where(m => m.BusinessId == quote.BusinessId && m.IsActive && (RoleSets.Managers.Contains(m.Role) || m.UserId == quote.CreatedBy))
                .Select(m => m.UserId)
                .ToListAsync(ct);
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var accepted = status == QuoteStatus.Accepted;
            foreach (var userId in recipients.Distinct())
            {
                notifications.Notify(userId, accepted ? NotificationTypes.QuoteAccepted : NotificationTypes.QuoteRejected,
                    $"{contactName} teklifi {(accepted ? "onayladı" : "reddetti")}",
                    $"{quote.No} · {ShareLinks.Money(quote.Total)}" + (note is null ? "" : $" · Not: {note}"),
                    $"/panel/teklifler/{quote.Id}");
            }

            scope.ServiceProvider.GetRequiredService<IAuditLogger>().Log(accepted ? AuditAction.QuoteAccepted : AuditAction.QuoteRejected,
                nameof(Quote), quote.Id, new { quote.No, via = "Link", note });
            await tenantDb.SaveChangesAsync(ct);
        }
        else
        {
            var current = await tenantDb.Quotes.AsNoTracking().Where(q => q.Id == quote.Id).Select(q => q.Status).FirstAsync(ct);
            var sameAnswer = current == status || (status == QuoteStatus.Accepted && current == QuoteStatus.Converted);
            if (!sameAnswer)
            {
                return Problems.Conflict("quote_answered", current switch
                {
                    QuoteStatus.Rejected => "Bu teklif daha önce reddedildi. Fikriniz değiştiyse işletmeyle iletişime geçin.",
                    QuoteStatus.Accepted or QuoteStatus.Converted => "Bu teklif daha önce onaylandı. Değişiklik için işletmeyle iletişime geçin.",
                    _ => "Bu teklif şu an cevaplanamıyor. İşletmeyle iletişime geçin.",
                });
            }
        }

        return TypedResults.Ok(await ResponseAsync(tenantDb, quote.Id, clock, ct));
    }
}
