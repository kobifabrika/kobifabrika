using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Sales;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Email;
using KobiFabrika.Infrastructure.Pdf;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Security;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Api.Features.Quotes;

/// <summary>
/// Teklifler (TKL-2026-0001). Hazırlama, paylaşma ve satışa dönüştürme Sahip, Yönetici, Personel (Personel sadece kendi
/// tekliflerini görür); Mali Müşavir salt okunur. Herkese açık link (/teklif/[token]) tahsilat linkiyle aynı yapıda:
/// "Paylaş" geçerli linki aynen gösterir, süresi dolmuşsa yeni link üretip geçerliliği 15 gün uzatır; "Yenile" eski linki
/// bilerek geçersiz kılar. Müşteri onaylar/reddeder; onaylanan teklif tek adımda (ödeme seçilerek) satışa döner.
/// </summary>
public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes").WithTags("Quotes").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Teklifler (status, contactId, q: no veya müşteri; sayfalı)");
        group.MapGet("/{id:guid}", Get).WithSummary("Teklif");
        group.MapPost("/", Create).RequireAuthorization(Policies.CanWrite).Validate<QuoteRequest>()
            .Produces<QuoteResponse>(StatusCodes.Status201Created).WithSummary("Yeni teklif (taslak)");
        group.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.CanWrite).Validate<QuoteRequest>()
            .Produces<QuoteResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Teklifi düzenle (müşteri cevap vermeden önce)");
        group.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.CanWrite).WithSummary("Teklifi sil (satışa dönüşmemişse; link kapanır)");
        group.MapPost("/{id:guid}/share", (Guid id, ShareQuoteRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit,
                EmailDispatch email, LinkTokenProtector protector, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct) =>
                ShareOrRenewAsync(id, request.SendEmail, false, member, db, audit, email, protector, options, clock, ct))
            .RequireAuthorization(Policies.CanWrite).WithSummary("Paylaş: geçerli link aynen döner; süresi dolmuşsa yeni link, geçerlilik 15 gün uzar");
        group.MapPost("/{id:guid}/renew", (Guid id, ShareQuoteRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit,
                EmailDispatch email, LinkTokenProtector protector, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct) =>
                ShareOrRenewAsync(id, request.SendEmail, true, member, db, audit, email, protector, options, clock, ct))
            .RequireAuthorization(Policies.CanWrite).WithSummary("Linki yenile: eski link geçersiz olur, geçerlilik bugünden 15 gün");
        group.MapPost("/{id:guid}/mark-accepted", (Guid id, RespondQuoteRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit,
                TimeProvider clock, CancellationToken ct) => MarkAsync(id, QuoteStatus.Accepted, request, member, db, audit, clock, ct))
            .RequireAuthorization(Policies.CanWrite).Validate<RespondQuoteRequest>().WithSummary("Müşteri onayladı (ör. telefonla)");
        group.MapPost("/{id:guid}/mark-rejected", (Guid id, RespondQuoteRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit,
                TimeProvider clock, CancellationToken ct) => MarkAsync(id, QuoteStatus.Rejected, request, member, db, audit, clock, ct))
            .RequireAuthorization(Policies.CanWrite).Validate<RespondQuoteRequest>().WithSummary("Müşteri reddetti");
        group.MapPost("/{id:guid}/convert", Convert).RequireAuthorization(Policies.CanWrite).Validate<ConvertQuoteRequest>()
            .Produces<SaleCreatedResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Onaylanan teklifi satışa dönüştür (ödeme boşsa tamamı veresiye)");
        group.MapGet("/{id:guid}/pdf", Pdf).WithSummary("Logolu teklif PDF'i");

        return app;
    }

    internal static ProblemHttpResult QuoteNotFound() => Problems.NotFound("Teklif bulunamadı.");

    private static IQueryable<Quote> VisibleTo(this IQueryable<Quote> query, CurrentMember member) =>
        member.CanViewAllSales ? query : query.Where(q => q.CreatedBy == member.UserId);

    /// <summary>Cevap bekleyen (taslak veya gönderilmiş) teklifin geçerlilik günü geçti mi.</summary>
    internal static bool IsExpired(QuoteStatus status, DateOnly validUntil, DateOnly today) =>
        status is QuoteStatus.Draft or QuoteStatus.Sent && validUntil < today;

    internal static async Task<QuoteResponse?> LoadAsync(AppDbContext db, CurrentMember? member, Guid id, TimeProvider clock, CancellationToken ct)
    {
        var query = db.Quotes.AsNoTracking();
        if (member is not null)
        {
            query = query.VisibleTo(member);
        }

        var quote = await query.Include(q => q.Lines.OrderBy(l => l.SortOrder)).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return null;
        }

        var contact = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(c => c.Id == quote.ContactId, ct);
        var createdByName = await db.Users.Where(u => u.Id == quote.CreatedBy).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        var saleNo = quote.SaleId is { } saleId ? await db.Sales.Where(s => s.Id == saleId).Select(s => s.No).FirstOrDefaultAsync(ct) : null;

        return new QuoteResponse(
            quote.Id, quote.No, quote.ContactId, contact.DisplayName, contact.Phone, contact.Email, quote.Date, quote.ValidUntil, quote.Status,
            IsExpired(quote.Status, quote.ValidUntil, BusinessTime.Today(clock)), quote.TokenHash is not null,
            quote.SubTotal, quote.LineDiscountTotal, quote.DiscountTotal, quote.VatTotal, quote.Total, DocumentLines.Vat(quote.Lines),
            quote.Lines.Select(DocumentLines.ToResponse).ToList(), quote.Notes, quote.SentAt, quote.LastViewedAt, quote.RespondedAt,
            quote.ResponseNote, quote.RespondedVia, quote.SaleId, saleNo, quote.CreatedBy, createdByName, quote.CreatedAt);
    }

    private static async Task<Ok<QuoteListResponse>> List(
        CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct,
        QuoteStatus? status = null, Guid? contactId = null, string? q = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Quotes.AsNoTracking().VisibleTo(member);
        if (status is { } s)
        {
            query = query.Where(x => x.Status == s);
        }

        if (contactId is { } cid)
        {
            query = query.Where(x => x.ContactId == cid);
        }

        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(x => x.No.Contains(term)
                || db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Any(c => c.Id == x.ContactId && c.DisplayName.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var today = BusinessTime.Today(clock);
        var rows = await query.OrderByDescending(x => x.Year).ThenByDescending(x => x.Seq)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.No, x.ContactId, x.Date, x.ValidUntil, x.Status, x.Total,
                ContactName = db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == x.ContactId).Select(c => c.DisplayName).First(),
                CreatedByName = db.Users.Where(u => u.Id == x.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new QuoteListResponse(rows.Select(r => new QuoteListItem(
            r.Id, r.No, r.ContactId, r.ContactName, r.Date, r.ValidUntil, r.Status, IsExpired(r.Status, r.ValidUntil, today), r.Total,
            r.CreatedByName)).ToList(), total));
    }

    private static async Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> Get(
        Guid id, CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        await LoadAsync(db, member, id, clock, ct) is { } quote ? TypedResults.Ok(quote) : QuoteNotFound();

    private static void ApplyTotals(Quote quote, CalcResult totals)
    {
        quote.SubTotal = totals.SubTotal;
        quote.LineDiscountTotal = totals.LineDiscountTotal;
        quote.DiscountTotal = totals.DiscountTotal;
        quote.VatTotal = totals.VatTotal;
        quote.Total = totals.Total;
    }

    private static async Task<IResult> Create(
        QuoteRequest request, CurrentMember member, AppDbContext db, LedgerService transactions, DocumentNumbers numbers, IAuditLogger audit,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Contacts.AnyAsync(c => c.Id == request.ContactId, ct))
        {
            return Validation.FieldProblem("contactId", "Müşteri bulunamadı.");
        }

        var (document, field, error) = await DocumentLines.ResolveAsync(db, request.Lines!, request.Discount, allowInactive: false, ct);
        if (document is null)
        {
            return Validation.FieldProblem(field!, error!);
        }

        var date = request.Date ?? BusinessTime.Today(clock);
        var quote = await transactions.InTransactionAsync(async () =>
        {
            var (year, seq, no) = await numbers.NextAsync(DocumentKind.Quote, BusinessTime.StartOfDayUtc(date).AddHours(12), ct);
            var quote = new Quote
            {
                Year = year,
                Seq = seq,
                No = no,
                ContactId = request.ContactId,
                Date = date,
                ValidUntil = request.ValidUntil ?? date.AddDays(Quote.ValidityDays),
                Status = QuoteStatus.Draft,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            };
            ApplyTotals(quote, document.Totals);
            quote.Lines.AddRange(document.Lines.Select((l, i) => DocumentLines.ToEntity<QuoteLine>(l, i)));
            db.Quotes.Add(quote);
            audit.Log(AuditAction.QuoteCreated, nameof(Quote), quote.Id, new { no, contactId = request.ContactId, total = quote.Total });
            await db.SaveChangesAsync(ct);
            return quote;
        }, ct);

        return TypedResults.Created($"/api/quotes/{quote.Id}", await LoadAsync(db, member, quote.Id, clock, ct));
    }

    private static ProblemHttpResult Locked(QuoteStatus status) => status switch
    {
        QuoteStatus.Converted => Problems.Conflict("quote_converted", "Bu teklif satışa dönüştü; değiştirilemez."),
        _ => Problems.Conflict("quote_answered", "Müşteri bu teklife cevap verdi; değiştirilemez. Gerekirse yeni teklif hazırlayın."),
    };

    private static async Task<IResult> Update(
        Guid id, QuoteRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var quote = await db.Quotes.VisibleTo(member).Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return QuoteNotFound();
        }

        if (!quote.IsEditable)
        {
            return Locked(quote.Status);
        }

        if (!await db.Contacts.AnyAsync(c => c.Id == request.ContactId, ct))
        {
            return Validation.FieldProblem("contactId", "Müşteri bulunamadı.");
        }

        var (document, field, error) = await DocumentLines.ResolveAsync(db, request.Lines!, request.Discount, allowInactive: false, ct);
        if (document is null)
        {
            return Validation.FieldProblem(field!, error!);
        }

        db.QuoteLines.RemoveRange(quote.Lines);
        db.QuoteLines.AddRange(document.Lines.Select((l, i) =>
        {
            var line = DocumentLines.ToEntity<QuoteLine>(l, i);
            line.QuoteId = quote.Id;
            return line;
        }));
        quote.ContactId = request.ContactId;
        quote.Date = request.Date ?? quote.Date;
        quote.ValidUntil = request.ValidUntil ?? quote.ValidUntil;
        quote.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        ApplyTotals(quote, document.Totals);
        audit.Log(AuditAction.QuoteUpdated, nameof(Quote), id, new { quote.No, total = quote.Total });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, member, id, clock, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, CurrentMember member, AppDbContext db, IAuditLogger audit, CancellationToken ct)
    {
        var quote = await db.Quotes.VisibleTo(member).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return QuoteNotFound();
        }

        if (quote.Status == QuoteStatus.Converted)
        {
            return Locked(quote.Status);
        }

        db.Quotes.Remove(quote);
        audit.Log(AuditAction.QuoteDeleted, nameof(Quote), id, new { quote.No });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<QuoteLinkResponse>, ProblemHttpResult>> ShareOrRenewAsync(
        Guid id, bool sendEmail, bool renew, CurrentMember member, AppDbContext db, IAuditLogger audit, EmailDispatch email,
        LinkTokenProtector protector, IOptions<AppOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var quote = await db.Quotes.VisibleTo(member).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return QuoteNotFound();
        }

        if (quote.Status == QuoteStatus.Converted)
        {
            return Locked(quote.Status);
        }

        var today = BusinessTime.Today(clock);
        var expired = quote.ValidUntil < today;
        if ((expired || renew) && !quote.IsEditable)
        {
            return Problems.Conflict("quote_answered", "Müşteri bu teklife cevap verdi; linki yenilenemez. Gerekirse yeni teklif hazırlayın.");
        }

        var contact = await db.Contacts.AsNoTracking().FirstAsync(c => c.Id == quote.ContactId, ct);
        if (sendEmail && string.IsNullOrEmpty(contact.Email))
        {
            return Problems.BadRequest("email_required", "Müşterinin e-posta adresi yok. Önce kartına e-posta ekleyin.");
        }

        var token = renew || expired || quote.TokenHash is null ? null : protector.TryUnprotect(quote.ProtectedToken, quote.TokenHash);
        var isNew = token is null;
        if (token is null)
        {
            (token, var hash) = SecureTokens.Create();
            quote.TokenHash = hash;
            quote.ProtectedToken = protector.Protect(token);
            if (renew || expired)
            {
                quote.ValidUntil = today.AddDays(Quote.ValidityDays) is var extended && extended > quote.ValidUntil ? extended : quote.ValidUntil;
            }

            audit.Log(renew ? AuditAction.QuoteRenewed : AuditAction.QuoteShared, nameof(Quote), id, new { quote.No, validUntil = quote.ValidUntil });
        }

        if (quote.Status == QuoteStatus.Draft)
        {
            quote.Status = QuoteStatus.Sent;
        }

        quote.SentAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == member.BusinessId, ct);
        var url = options.Value.Link($"/teklif/{token}");
        var sent = sendEmail && await email.TrySendAsync(EmailTemplates.Quote(contact.Email!, business.Name, contact.DisplayName, quote.No,
            ShareLinks.Money(quote.Total), quote.ValidUntil.ToString("dd.MM.yyyy"), url), ct);
        var response = (await LoadAsync(db, member, id, clock, ct))!;
        return TypedResults.Ok(new QuoteLinkResponse(response, url,
            ShareLinks.WhatsApp(contact.Phone, ShareLinks.QuoteMessage(contact.DisplayName, business.Name, quote.No, quote.Total, url)), sent, isNew));
    }

    private static async Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> MarkAsync(
        Guid id, QuoteStatus status, RespondQuoteRequest request, CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock,
        CancellationToken ct)
    {
        var quote = await db.Quotes.AsNoTracking().VisibleTo(member).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return QuoteNotFound();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var changed = await db.Quotes.Where(q => q.Id == id && (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent))
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.Status, status)
                .SetProperty(q => q.RespondedAt, now)
                .SetProperty(q => q.RespondedVia, QuoteResponseChannel.Panel)
                .SetProperty(q => q.ResponseNote, note)
                .SetProperty(q => q.UpdatedAt, now), ct);
        if (changed != 1)
        {
            return Locked(quote.Status);
        }

        audit.Log(status == QuoteStatus.Accepted ? AuditAction.QuoteAccepted : AuditAction.QuoteRejected, nameof(Quote), id,
            new { quote.No, via = "Panel", note });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await LoadAsync(db, member, id, clock, ct))!);
    }

    /// <summary>Teklifin kayıtlı kalemlerini (fiyatlar teklifteki gibi) satış belgesine çevirir.</summary>
    private static async Task<ResolvedDocument> FromQuoteAsync(AppDbContext db, Quote quote, CancellationToken ct)
    {
        var productIds = quote.Lines.Select(l => l.ProductId).OfType<Guid>().Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var lines = quote.Lines.OrderBy(l => l.SortOrder).Select(l => new ResolvedLine(
            l.ProductId is { } pid ? products.GetValueOrDefault(pid) : null, l.Description, l.Unit,
            new CalcLine(l.Quantity, l.UnitPrice, l.VatRate, SaleCalculator.Round(l.Quantity * l.UnitPrice), l.LineDiscount, l.AllocatedDiscount,
                l.LineTotal, l.VatAmount))).ToList();
        var totals = new CalcResult(lines.Select(l => l.Calc).ToList(), quote.SubTotal, quote.LineDiscountTotal, quote.DiscountTotal, quote.VatTotal,
            quote.Total, DocumentLines.Vat(quote.Lines));
        return new ResolvedDocument(lines, totals);
    }

    private static async Task<IResult> Convert(
        Guid id, ConvertQuoteRequest request, CurrentMember member, AppDbContext db, LedgerService transactions, SaleService sales, IAuditLogger audit,
        TimeProvider clock, CancellationToken ct)
    {
        var quote = await db.Quotes.AsNoTracking().VisibleTo(member).Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return QuoteNotFound();
        }

        if (quote.Status != QuoteStatus.Accepted)
        {
            return quote.Status == QuoteStatus.Converted
                ? Problems.Conflict("quote_converted", "Bu teklif zaten satışa dönüştü.")
                : Problems.Conflict("quote_not_accepted", "Sadece onaylanan teklif satışa dönüştürülebilir.");
        }

        var document = await FromQuoteAsync(db, quote, ct);
        var requested = request.Payments is { Count: > 0 } p ? p : [new SalePaymentRequest(PaymentMethod.Credit, quote.Total, null)];
        var (payments, field, error) = await sales.ResolvePaymentsAsync(quote.Total == 0 ? [] : requested, quote.Total, quote.ContactId, ct);
        if (payments is null)
        {
            return Validation.FieldProblem(field!, error!);
        }

        var result = await transactions.InTransactionAsync<(Sale Sale, List<string> Warnings)?>(async () =>
        {
            // Koşullu güncelleme: aynı teklif iki kez satışa dönüşmez.
            var changed = await db.Quotes.Where(q => q.Id == id && q.Status == QuoteStatus.Accepted)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuoteStatus.Converted), ct);
            if (changed != 1)
            {
                return null;
            }

            var created = await sales.CreateAsync(new SaleDraft(
                quote.ContactId, clock.GetUtcNow().UtcDateTime, document, payments, request.Note ?? $"Teklif {quote.No}", quote.Id), ct);
            await db.Quotes.Where(q => q.Id == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.SaleId, created.Sale.Id), ct);
            audit.Log(AuditAction.QuoteConverted, nameof(Quote), id, new { quote.No, sale = created.Sale.No, total = quote.Total });
            await db.SaveChangesAsync(ct);
            return created;
        }, ct);

        if (result is not { } created)
        {
            return Problems.Conflict("quote_converted", "Bu teklif zaten satışa dönüştü.");
        }

        return TypedResults.Created($"/api/sales/{created.Sale.Id}",
            new SaleCreatedResponse((await SaleEndpoints.LoadAsync(db, member, created.Sale.Id, ct))!, created.Warnings));
    }

    /// <summary>Teklif PDF'inin verisi (panel ve herkese açık link).</summary>
    internal static async Task<QuoteDocumentData> DocumentAsync(
        AppDbContext db, IFileStorage storage, QuoteResponse quote, Guid businessId, TimeProvider clock, CancellationToken ct)
    {
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == businessId, ct);
        var contact = await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().FirstAsync(c => c.Id == quote.ContactId, ct);
        var address = string.Join(", ", new[] { contact.Address, contact.District, contact.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new QuoteDocumentData(
            await SaleEndpoints.PdfBusinessAsync(business, storage, ct), quote.No, quote.Date, quote.ValidUntil,
            new StatementParty(contact.DisplayName, contact.Phone is { } phone ? ContactSearchProvider.FormatPhone(phone) : null, contact.Email,
                address.Length > 0 ? address : null),
            quote.Lines.Select(l => new PdfLine(l.Description, Catalog.CatalogLabels.Quantity(l.Quantity, l.Unit), l.UnitPrice, l.LineDiscount, l.VatRate,
                l.LineTotal + l.AllocatedDiscount + l.LineDiscount)).ToList(),
            SaleEndpoints.Totals(quote.SubTotal, quote.LineDiscountTotal + quote.DiscountTotal, quote.VatTotal, quote.Total, quote.Vat),
            quote.Notes, BusinessTime.ToLocal(clock.GetUtcNow().UtcDateTime));
    }

    internal static string FileName(string no) => $"teklif-{no}.pdf";

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Pdf(
        Guid id, CurrentMember member, AppDbContext db, IFileStorage storage, IPdfRenderer pdf, TimeProvider clock, CancellationToken ct)
    {
        if (await LoadAsync(db, member, id, clock, ct) is not { } quote)
        {
            return QuoteNotFound();
        }

        var data = await DocumentAsync(db, storage, quote, member.RequiredBusinessId, clock, ct);
        return TypedResults.File(pdf.Quote(data), "application/pdf", FileName(quote.No));
    }
}
