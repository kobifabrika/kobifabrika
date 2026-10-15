using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Catalog;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Staff;
using KobiFabrika.Domain.Appointments;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Pdf;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Sales;

/// <summary>
/// Satışlar. Satış yapma Sahip, Yönetici, Personel; iptal Sahip ve Yönetici. Personel sadece kendi girdiği satışları görür
/// (liste, toplam, detay, fiş, arama); Mali Müşavir hepsini salt okunur görür ve Excel'e aktarır.
/// </summary>
public static class SaleEndpoints
{
    public static IEndpointRouteBuilder MapSaleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sales").WithTags("Sales").RequireAuthorization(Policies.Panel);

        group.MapGet("/", List).WithSummary("Satışlar (from/to yerel gün, q: no veya müşteri, status, contactId, method; sayfalı + dönem toplamı)");
        group.MapGet("/{id:guid}", Get).WithSummary("Satış detayı");
        group.MapPost("/", Create).RequireAuthorization(Policies.CanWrite).Validate<SaleRequest>()
            .Produces<SaleCreatedResponse>(StatusCodes.Status201Created)
            .WithSummary("Satış yap: stok düşer, veresiye cariye, nakit kasaya, kart/havale bankaya");
        group.MapPost("/{id:guid}/cancel", Cancel).RequireAuthorization(Policies.ManageFinance).Validate<CancelSaleRequest>()
            .WithSummary("Satışı iptal et: stok, kasa ve cari ters kayıtla geri alınır; kayıt silinmez");
        group.MapGet("/{id:guid}/receipt.pdf", Receipt).WithSummary("Fiş (PDF; işletmenin fiş genişliğinde, 58 veya 80 mm)");
        group.MapGet("/export.xlsx", Export).RequireAuthorization(Policies.Export).WithSummary("Satışlar Excel");

        return app;
    }

    internal static ProblemHttpResult SaleNotFound() => Problems.NotFound("Satış bulunamadı.");

    /// <summary>Personel sadece kendi girdiği satışları görür.</summary>
    internal static IQueryable<Sale> VisibleTo(this IQueryable<Sale> query, CurrentMember member) =>
        member.CanViewAllSales ? query : query.Where(s => s.CreatedBy == member.UserId);

    internal static async Task<SaleResponse?> LoadAsync(AppDbContext db, CurrentMember member, Guid id, CancellationToken ct)
    {
        var sale = await db.Sales.AsNoTracking().VisibleTo(member)
            .Include(s => s.Lines.OrderBy(l => l.SortOrder))
            .Include(s => s.Payments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sale is null)
        {
            return null;
        }

        var contact = sale.ContactId is { } contactId
            ? await db.Contacts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().Where(c => c.Id == contactId)
                .Select(c => new { c.DisplayName, c.Phone }).FirstOrDefaultAsync(ct)
            : null;
        var userIds = new[] { sale.CreatedBy, sale.CancelledBy }.OfType<Guid>().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var accountIds = sale.Payments.Select(p => p.CashAccountId).OfType<Guid>().ToList();
        var accounts = await db.CashAccounts.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var quoteNo = sale.QuoteId is { } quoteId ? await db.Quotes.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(q => q.Id == quoteId)
            .Select(q => q.No).FirstOrDefaultAsync(ct) : null;
        var creditEntryIds = sale.Payments.Select(p => p.LedgerEntryId).OfType<Guid>().ToList();
        var dueDate = creditEntryIds.Count == 0 ? null : await db.LedgerEntries.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(e => creditEntryIds.Contains(e.Id)).Select(e => e.DueDate).FirstOrDefaultAsync(ct);
        var lineStaffIds = sale.Lines.Select(l => l.StaffMemberId).OfType<Guid>().Distinct().ToList();
        var staffNames = lineStaffIds.Count == 0 ? new Dictionary<Guid, string>() : await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(s => lineStaffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var staffName = sale.Lines.Select(l => l.StaffMemberId).OfType<Guid>().Select(staffNames.GetValueOrDefault).FirstOrDefault();

        return new SaleResponse(
            sale.Id, sale.No, sale.Date, sale.ContactId, contact?.DisplayName, contact?.Phone, sale.Status,
            sale.SubTotal, sale.LineDiscountTotal, sale.DiscountTotal, DiscountSpec.From(sale.DiscountType, sale.DiscountValue), sale.VatTotal,
            sale.Total, DocumentLines.Vat(sale.Lines),
            sale.Lines.Select(l => DocumentLines.ToResponse(l) with
            {
                StaffMemberId = l.StaffMemberId, StaffName = l.StaffMemberId is { } ls ? staffNames.GetValueOrDefault(ls) : null,
            }).ToList(),
            sale.Payments.OrderBy(p => p.Method).Select(p => new SalePaymentResponse(
                p.Id, p.Method, p.Amount, p.CashAccountId, p.CashAccountId is { } a ? accounts.GetValueOrDefault(a) : null)).ToList(),
            sale.Note, sale.CreatedBy, sale.CreatedBy is { } c ? users.GetValueOrDefault(c) : null,
            sale.CancelledAt, sale.CancelledBy is { } cb ? users.GetValueOrDefault(cb) : null, sale.CancelReason, sale.QuoteId, quoteNo, dueDate,
            sale.AppointmentId, staffName);
    }

    private static IQueryable<Sale> Filter(
        AppDbContext db, CurrentMember member, DateOnly? from, DateOnly? to, string? q, SaleStatus? status, Guid? contactId, PaymentMethod? method)
    {
        var query = db.Sales.AsNoTracking().VisibleTo(member);
        if (from is { } f)
        {
            var start = BusinessTime.StartOfDayUtc(f);
            query = query.Where(s => s.Date >= start);
        }

        if (to is { } t)
        {
            var end = BusinessTime.StartOfDayUtc(t.AddDays(1));
            query = query.Where(s => s.Date < end);
        }

        if (status is { } st)
        {
            query = query.Where(s => s.Status == st);
        }

        if (contactId is { } cid)
        {
            query = query.Where(s => s.ContactId == cid);
        }

        if (method is { } m)
        {
            query = query.Where(s => s.Payments.Any(p => p.Method == m));
        }

        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(s => s.No.Contains(term)
                || db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Any(c => c.Id == s.ContactId && c.DisplayName.Contains(term)));
        }

        return query;
    }

    private static async Task<Ok<SaleListResponse>> List(
        CurrentMember member, AppDbContext db, CancellationToken ct, DateOnly? from = null, DateOnly? to = null, string? q = null,
        SaleStatus? status = null, Guid? contactId = null, PaymentMethod? method = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Filter(db, member, from, to, q, status, contactId, method);

        var total = await query.CountAsync(ct);
        var completed = query.Where(s => s.Status == SaleStatus.Completed);
        var sum = await completed.SumAsync(s => (decimal?)s.Total, ct) ?? 0;
        var count = await completed.CountAsync(ct);
        var rows = await query.OrderByDescending(s => s.Date).ThenByDescending(s => s.Seq)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                s.Id, s.No, s.Date, s.ContactId, s.Status, s.Total,
                ContactName = db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == s.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                Methods = s.Payments.Select(p => p.Method).Distinct().ToList(),
                ItemCount = s.Lines.Count,
                CreatedByName = db.Users.Where(u => u.Id == s.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new SaleListItem(
            r.Id, r.No, r.Date, r.ContactId, r.ContactName, r.Status, r.Total, r.Methods.Order().ToList(), r.ItemCount, r.CreatedByName)).ToList();
        return TypedResults.Ok(new SaleListResponse(items, total, sum, count));
    }

    private static async Task<Results<Ok<SaleResponse>, ProblemHttpResult>> Get(Guid id, CurrentMember member, AppDbContext db, CancellationToken ct) =>
        await LoadAsync(db, member, id, ct) is { } sale ? TypedResults.Ok(sale) : SaleNotFound();

    private static async Task<IResult> Create(
        SaleRequest request, CurrentMember member, AppDbContext db, SaleService sales, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (request.Date is { } requested)
        {
            var date = DateTime.SpecifyKind(requested.ToUniversalTime(), DateTimeKind.Utc);
            if (!member.IsManager)
            {
                return Validation.FieldProblem("date", "Geçmiş tarihli satışı sadece Sahip ve Yönetici girebilir.");
            }

            if (date > now.AddMinutes(5) || date < now.AddYears(-1))
            {
                return Validation.FieldProblem("date", "Satış tarihi bugünden ileri ve 1 yıldan eski olamaz.");
            }
        }

        if (request.ContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId, ct))
        {
            return Validation.FieldProblem("contactId", "Müşteri bulunamadı.");
        }

        var (document, field, error) = await DocumentLines.ResolveAsync(db, request.Lines!, request.Discount, allowInactive: false, ct,
            allowPackages: true);
        if (document is null)
        {
            return Validation.FieldProblem(field!, error!);
        }

        if (request.ContactId is null && document.Lines.Any(l => l.Package is not null))
        {
            return Validation.FieldProblem("contactId", "Paket satışı için müşteri seçin; paket müşterinin kartına yazılır.");
        }

        var (payments, paymentField, paymentError) = await sales.ResolvePaymentsAsync(request.Payments ?? [], document.Totals.Total, request.ContactId, ct);
        if (payments is null)
        {
            return Validation.FieldProblem(paymentField!, paymentError!);
        }

        AppointmentLink? appointment = null;
        if (request.AppointmentId is { } appointmentId)
        {
            var scope = await StaffAccess.ScopeAsync(db, member, ct);
            var row = await db.Appointments.AsNoTracking()
                .Where(a => a.Id == appointmentId && (!scope.Restricted || a.StaffMemberId == scope.StaffId))
                .Select(a => new { a.ContactId, a.StaffMemberId, a.ProductId, a.Status, a.SaleId })
                .FirstOrDefaultAsync(ct);
            if (row is null)
            {
                return Validation.FieldProblem("appointmentId", "Randevu bulunamadı.");
            }

            if (row.SaleId is not null)
            {
                return Problems.Conflict("appointment_already_converted", "Bu randevu zaten satışa çevrildi.");
            }

            if (row.Status != AppointmentStatus.Completed)
            {
                return Problems.Conflict("appointment_not_completed", "Satışa çevirmek için önce randevuyu tamamlandı olarak işaretleyin.");
            }

            if (request.ContactId != row.ContactId)
            {
                return Validation.FieldProblem("contactId", "Randevudan çevrilen satışın müşterisi randevunun müşterisi olmalı.");
            }

            appointment = new AppointmentLink(appointmentId, row.StaffMemberId, row.ProductId);
        }

        var (lineStaff, overrides, staffField, staffError) = await LineStaffAsync(db, member, request.Lines!, document, appointment, ct);
        if (staffError is not null)
        {
            return Validation.FieldProblem(staffField!, staffError);
        }

        var dateUtc = request.Date is { } d ? DateTime.SpecifyKind(d.ToUniversalTime(), DateTimeKind.Utc) : now;
        if (SaleService.DueDateError(request.DueDate, BusinessTime.LocalDay(dateUtc)) is { } dueError)
        {
            return Validation.FieldProblem("dueDate", dueError);
        }

        var (sale, warnings) = await sales.CreateAsync(
            new SaleDraft(request.ContactId, dateUtc, document, payments, request.Note, null, request.DueDate, appointment, lineStaff, overrides), ct);
        return TypedResults.Created($"/api/sales/{sale.Id}", new SaleCreatedResponse((await LoadAsync(db, member, sale.Id, ct))!, warnings));
    }

    /// <summary>
    /// Satır personelleri: aktif personel olmalı (kiracı filtresi). Personel rolü başkasını da seçebilir (resepsiyon); varsayılandan
    /// (randevunun hizmet satırında randevunun personeli, diğerlerinde kendi bağlı kaydı) farklı seçim günlüğe yazılır.
    /// </summary>
    private static async Task<(List<Guid?> Staff, List<LineStaffOverride> Overrides, string? Field, string? Error)> LineStaffAsync(
        AppDbContext db, CurrentMember member, IReadOnlyList<DocumentLineRequest> lines, ResolvedDocument document, AppointmentLink? appointment,
        CancellationToken ct)
    {
        var requested = lines.Select(l => l.StaffMemberId).ToList();
        var ids = requested.OfType<Guid>().Distinct().ToList();
        var active = ids.Count == 0 ? new Dictionary<Guid, string>() : await db.StaffMembers.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.IsActive).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        for (var i = 0; i < requested.Count; i++)
        {
            if (requested[i] is { } id && !active.ContainsKey(id))
            {
                return ([], [], $"lines[{i}].staffMemberId", "Personel bulunamadı veya pasif.");
            }
        }

        var overrides = new List<LineStaffOverride>();
        if (member.Role == BusinessRole.Staff && ids.Count > 0)
        {
            var mine = await StaffAccess.MyStaffIdAsync(db, member, ct);
            var defaults = document.Lines.Select(l => appointment is { } a && l.Product?.Id == a.ProductId ? a.StaffMemberId : mine).ToList();
            var defaultIds = defaults.OfType<Guid>().Distinct().ToList();
            var names = await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
                .Where(s => defaultIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
            for (var i = 0; i < requested.Count; i++)
            {
                if (requested[i] is { } chosen && chosen != defaults[i])
                {
                    overrides.Add(new LineStaffOverride(i, defaults[i] is { } d ? names.GetValueOrDefault(d) : null, active[chosen]));
                }
            }
        }

        return (requested, overrides, null, null);
    }

    private static async Task<Results<Ok<SaleResponse>, ProblemHttpResult>> Cancel(
        Guid id, CancelSaleRequest request, CurrentMember member, AppDbContext db, SaleService sales, CancellationToken ct)
    {
        if (!await db.Sales.AnyAsync(s => s.Id == id, ct))
        {
            return SaleNotFound();
        }

        if (!await sales.CancelAsync(id, request.Reason, ct))
        {
            return Problems.Conflict("sale_cancelled", "Bu satış zaten iptal edilmiş.");
        }

        return TypedResults.Ok((await LoadAsync(db, member, id, ct))!);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Receipt(
        Guid id, CurrentMember member, AppDbContext db, IFileStorage storage, IPdfRenderer pdf, CancellationToken ct)
    {
        if (await LoadAsync(db, member, id, ct) is not { } sale)
        {
            return SaleNotFound();
        }

        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == member.BusinessId, ct);
        var data = new ReceiptData(
            await PdfBusinessAsync(business, storage, ct), sale.No, BusinessTime.ToLocal(sale.Date), sale.CreatedByName, sale.ContactName,
            sale.Lines.Select(l => new PdfLine(l.Description, CatalogLabels.Quantity(l.Quantity, l.Unit), l.UnitPrice, l.LineDiscount, l.VatRate, l.LineTotal + l.AllocatedDiscount + l.LineDiscount)).ToList(),
            Totals(sale.SubTotal, sale.LineDiscountTotal + sale.DiscountTotal, sale.VatTotal, sale.Total, sale.Vat),
            sale.Payments.Select(p => new PdfPayment(SaleLabels.Methods[p.Method], p.Amount)).ToList(),
            sale.Status == SaleStatus.Cancelled, sale.Note, business.ReceiptWidth);
        return TypedResults.File(pdf.Receipt(data), "application/pdf", $"fis-{sale.No}.pdf");
    }

    internal static PdfTotals Totals(decimal subTotal, decimal discount, decimal vatTotal, decimal total, IReadOnlyList<VatBreakdown> vat) =>
        new(subTotal, discount, vatTotal, total, vat.Select(v => new PdfVat(v.Rate, v.Base, v.Vat)).ToList());

    internal static async Task<PdfBusiness> PdfBusinessAsync(Domain.Businesses.Business business, IFileStorage storage, CancellationToken ct)
    {
        var logo = business.LogoUrl is { } url ? await storage.ReadAsync(url, ct) : null;
        var address = string.Join(", ", new[] { business.Address, business.District, business.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new PdfBusiness(business.Name, logo, business.Phone is { } p ? ContactSearchProvider.FormatPhone(p) : null, business.Email,
            address.Length > 0 ? address : null);
    }

    private static async Task<FileContentHttpResult> Export(
        CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null, string? q = null, SaleStatus? status = null)
    {
        var sales = await Filter(db, member, from, to, q, status, null, null)
            .OrderBy(s => s.Date)
            .Select(s => new
            {
                s.No, s.Date, s.Status, s.SubTotal, Discount = s.LineDiscountTotal + s.DiscountTotal, s.VatTotal, s.Total,
                ContactName = db.Contacts.IgnoreQueryFilters(new[] { QueryFilters.SoftDelete }).Where(c => c.Id == s.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                Payments = s.Payments.Select(p => new { p.Method, p.Amount }).ToList(),
                CreatedByName = db.Users.Where(u => u.Id == s.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Satışlar");
        LedgerEndpoints.Header(sheet, 1, "No", "Tarih", "Müşteri", "Durum", "Ara toplam", "İndirim", "KDV", "Toplam",
            "Nakit", "Kart", "Havale/EFT", "Veresiye", "Satan");
        var row = 2;
        foreach (var s in sales)
        {
            decimal Paid(PaymentMethod m) => s.Payments.Where(p => p.Method == m).Sum(p => p.Amount);
            object?[] values =
            [
                s.No, BusinessTime.ToLocal(s.Date), s.ContactName, s.Status == SaleStatus.Cancelled ? "İptal" : "Tamamlandı",
                s.SubTotal, s.Discount, s.VatTotal, s.Total,
                Paid(PaymentMethod.Cash), Paid(PaymentMethod.Card), Paid(PaymentMethod.Transfer), Paid(PaymentMethod.Credit), s.CreatedByName,
            ];
            for (var i = 0; i < values.Length; i++)
            {
                sheet.Cell(row, i + 1).Value = XLCellValue.FromObject(values[i]);
            }

            row++;
        }

        sheet.Column(2).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
        for (var column = 5; column <= 12; column++)
        {
            sheet.Column(column).Style.NumberFormat.Format = "#,##0.00";
        }

        sheet.Columns().AdjustToContents();
        audit.Log(AuditAction.SalesExported, nameof(Sale), null, new { count = sales.Count, from, to });
        await db.SaveChangesAsync(ct);
        return TypedResults.File(LedgerEndpoints.Save(book), LedgerEndpoints.XlsxContentType, $"satislar-{BusinessTime.Today(clock):yyyy-MM-dd}.xlsx");
    }
}
