using System.Globalization;
using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Finance;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Reports;

public enum ReportGroupBy
{
    Day,
    Week,
    Month,
}

public enum TopProductSort
{
    Quantity,
    Amount,
}

/// <summary>Dönem satırı: tamamlanan satışlar (iptaller toplamda yok, ayrı gösterilir) ve ödeme tipine göre dağılım.</summary>
public sealed record SalesReportRow(
    DateOnly PeriodStart, string Label, int Count, decimal Total, decimal Vat, decimal Cash, decimal Card, decimal Transfer, decimal Credit,
    int CancelledCount, decimal CancelledTotal);

public sealed record SalesReportResponse(DateOnly From, DateOnly To, ReportGroupBy GroupBy, IReadOnlyList<SalesReportRow> Rows, SalesReportRow Totals);

/// <summary>Satır türü: katalogdaki ürün, hizmet veya katalogda olmayan serbest kalem.</summary>
public enum TopProductKind
{
    Product,
    Service,
    Free,
}

public sealed record TopProductRow(Guid? ProductId, string Name, TopProductKind Kind, ProductUnit Unit, decimal Quantity, decimal Amount, int SaleCount);

public sealed record TopProductsResponse(DateOnly From, DateOnly To, TopProductSort Sort, IReadOnlyList<TopProductRow> Rows);

/// <summary>Alacak yaşlandırma: vadesi gelmemiş ve vadeden bu yana geçen güne göre kovalar.</summary>
public sealed record ReceivableAgingRow(
    Guid ContactId, string Name, string? Phone, decimal Balance, decimal NotDue, decimal Days0To30, decimal Days31To60, decimal Days61To90,
    decimal Days90Plus, DateOnly? OldestDueDate);

public sealed record ReceivablesReportResponse(DateOnly AsOf, IReadOnlyList<ReceivableAgingRow> Rows, ReceivableAgingRow Totals);

public sealed record IncomeExpenseLine(string Name, bool IsSales, IReadOnlyList<decimal> Values, decimal Total);

/// <summary>Satışların aylık KDV kırılımı (mali müşavir için).</summary>
public sealed record SalesVatMonth(string Month, decimal Net, decimal Vat, decimal Gross);

/// <summary>
/// Gelir-gider: aylar × kategoriler. Gelirde ilk satır "Satışlar" (tamamlanan satışlar, satış tarihine göre, veresiye dahil, KDV dahil),
/// sonra gelir kategorileri; giderde kategoriler. Ters kayıtlar kendi kategorisinden, yapıldığı ayda düşülür. Transfer ve
/// tedarikçiye ödeme rapora girmez. Maaş satırı Yönetici'de "Personel giderleri" adıyla.
/// </summary>
public sealed record IncomeExpenseResponse(
    DateOnly From, DateOnly To, IReadOnlyList<string> Months, IReadOnlyList<IncomeExpenseLine> Income, IReadOnlyList<IncomeExpenseLine> Expense,
    IReadOnlyList<decimal> IncomeTotals, IReadOnlyList<decimal> ExpenseTotals, IReadOnlyList<decimal> Net, IReadOnlyList<SalesVatMonth> SalesVat);

/// <summary>
/// Raporlar: satışlar, en çok satan, alacaklar (yaşlandırma), gelir-gider; hepsi Excel'e aktarılır (Türkçe başlık, TL, İstanbul saati).
/// Görme: Sahip, Yönetici, Mali Müşavir (Personel göremez). Aralık İstanbul günleriyle (from/to dahil); boşsa bu ay.
/// </summary>
public static class ReportEndpoints
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization(Policies.ViewReports);
        group.MapGet("/sales", Sales).WithSummary("Satışlar (groupBy: Day/Week/Month; ödeme tipi kırılımı)");
        group.MapGet("/sales.xlsx", SalesXlsx).RequireAuthorization(Policies.Export).WithSummary("Satış raporu Excel");
        group.MapGet("/top-products", TopProducts).WithSummary("En çok satan ürün/hizmet (sort: Quantity/Amount)");
        group.MapGet("/top-products.xlsx", TopProductsXlsx).RequireAuthorization(Policies.Export).WithSummary("En çok satan Excel");
        group.MapGet("/receivables", Receivables).WithSummary("Alacaklar: müşteri bazında yaşlandırma (bugün itibarıyla)");
        group.MapGet("/receivables.xlsx", ReceivablesXlsx).RequireAuthorization(Policies.Export).WithSummary("Alacak raporu Excel");
        group.MapGet("/income-expense", IncomeExpense).WithSummary("Gelir-gider: aylık, kategori bazında");
        group.MapGet("/income-expense.xlsx", IncomeExpenseXlsx).RequireAuthorization(Policies.Export).WithSummary("Gelir-gider Excel (satışlarda KDV kırılımı)");
        return app;
    }

    internal static (DateOnly From, DateOnly To) Range(TimeProvider clock, DateOnly? from, DateOnly? to)
    {
        var today = BusinessTime.Today(clock);
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        // En fazla 5 yıl.
        return (start < end.AddYears(-5) ? end.AddYears(-5) : start, end);
    }

    private static DateOnly PeriodStart(DateOnly day, ReportGroupBy groupBy) => groupBy switch
    {
        ReportGroupBy.Week => BusinessTime.StartOfWeek(day),
        ReportGroupBy.Month => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    private static string PeriodLabel(DateOnly start, ReportGroupBy groupBy) => groupBy switch
    {
        ReportGroupBy.Week => $"{start.ToString("dd.MM", Tr)} – {start.AddDays(6).ToString("dd.MM.yyyy", Tr)}",
        ReportGroupBy.Month => start.ToString("MMMM yyyy", Tr),
        _ => start.ToString("dd.MM.yyyy", Tr),
    };

    private static string MonthKey(DateOnly d) => $"{d.Year:D4}-{d.Month:D2}";

    private static string MonthLabel(string key) =>
        new DateOnly(int.Parse(key[..4], CultureInfo.InvariantCulture), int.Parse(key[5..], CultureInfo.InvariantCulture), 1).ToString("MMMM yyyy", Tr);

    // ---- Satışlar ----

    internal static async Task<SalesReportResponse> BuildSalesAsync(AppDbContext db, DateOnly from, DateOnly to, ReportGroupBy groupBy, CancellationToken ct)
    {
        var (start, end) = BusinessTime.RangeUtc(from, to);
        var sales = await db.Sales.AsNoTracking().Where(s => s.Date >= start && s.Date < end)
            .Select(s => new { s.Id, s.Date, s.Total, s.VatTotal, s.Status }).ToListAsync(ct);
        var payments = await db.SalePayments.AsNoTracking()
            .Where(p => db.Sales.Any(s => s.Id == p.SaleId && s.Status == SaleStatus.Completed && s.Date >= start && s.Date < end))
            .Select(p => new { p.SaleId, p.Method, p.Amount }).ToListAsync(ct);
        var paymentsBySale = payments.GroupBy(p => p.SaleId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = sales.GroupBy(s => PeriodStart(BusinessTime.LocalDay(s.Date), groupBy)).OrderBy(g => g.Key).Select(g =>
        {
            var done = g.Where(s => s.Status == SaleStatus.Completed).ToList();
            var cancelled = g.Where(s => s.Status == SaleStatus.Cancelled).ToList();
            var pays = done.SelectMany(s => paymentsBySale.GetValueOrDefault(s.Id) ?? []).ToList();
            decimal By(PaymentMethod m) => pays.Where(p => p.Method == m).Sum(p => p.Amount);
            return new SalesReportRow(g.Key, PeriodLabel(g.Key, groupBy), done.Count, done.Sum(s => s.Total), done.Sum(s => s.VatTotal),
                By(PaymentMethod.Cash), By(PaymentMethod.Card), By(PaymentMethod.Transfer), By(PaymentMethod.Credit),
                cancelled.Count, cancelled.Sum(s => s.Total));
        }).ToList();

        var totals = new SalesReportRow(from, "Toplam", rows.Sum(r => r.Count), rows.Sum(r => r.Total), rows.Sum(r => r.Vat), rows.Sum(r => r.Cash),
            rows.Sum(r => r.Card), rows.Sum(r => r.Transfer), rows.Sum(r => r.Credit), rows.Sum(r => r.CancelledCount), rows.Sum(r => r.CancelledTotal));
        return new SalesReportResponse(from, to, groupBy, rows, totals);
    }

    private static async Task<Ok<SalesReportResponse>> Sales(
        AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null, ReportGroupBy groupBy = ReportGroupBy.Day)
    {
        var (f, t) = Range(clock, from, to);
        return TypedResults.Ok(await BuildSalesAsync(db, f, t, groupBy, ct));
    }

    private static async Task<FileContentHttpResult> SalesXlsx(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null,
        ReportGroupBy groupBy = ReportGroupBy.Day)
    {
        var (f, t) = Range(clock, from, to);
        var report = await BuildSalesAsync(db, f, t, groupBy, ct);
        using var book = new XLWorkbook();
        var sheet = Sheet(book, "Satışlar", "Satış raporu", f, t, clock);
        Xlsx.Header(sheet, 4, "Dönem", "Satış adedi", "Toplam (KDV dahil)", "KDV", "Nakit", "Kart", "Havale/EFT", "Veresiye", "İptal adedi", "İptal tutarı");
        var row = 5;
        foreach (var r in report.Rows.Append(report.Totals))
        {
            Xlsx.Row(sheet, row++, r.Label, r.Count, r.Total, r.Vat, r.Cash, r.Card, r.Transfer, r.Credit, r.CancelledCount, r.CancelledTotal);
        }

        sheet.Row(row - 1).Style.Font.Bold = true;
        Xlsx.Format(sheet, 5, row - 1, money: [3, 4, 5, 6, 7, 8, 10]);
        return await FileAsync(db, audit, book, "satislar", f, t, ct);
    }

    // ---- En çok satan ----

    internal static async Task<TopProductsResponse> BuildTopProductsAsync(
        AppDbContext db, DateOnly from, DateOnly to, TopProductSort sort, ProductType? type, int take, CancellationToken ct)
    {
        var (start, end) = BusinessTime.RangeUtc(from, to);
        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => db.Sales.Any(s => s.Id == l.SaleId && s.Status == SaleStatus.Completed && s.Date >= start && s.Date < end))
            .Select(l => new { l.ProductId, l.Description, l.Unit, l.Quantity, l.LineTotal, l.SaleId })
            .ToListAsync(ct);
        var productIds = lines.Select(l => l.ProductId).OfType<Guid>().Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Type }, ct);

        var rows = lines
            .GroupBy(l => l.ProductId is { } id ? id.ToString() : "free:" + l.Description.Trim().ToLower(Tr))
            .Select(g =>
            {
                var first = g.First();
                var product = first.ProductId is { } id ? products.GetValueOrDefault(id) : null;
                var kind = product?.Type switch { ProductType.Product => TopProductKind.Product, ProductType.Service => TopProductKind.Service, _ => TopProductKind.Free };
                return new TopProductRow(first.ProductId, product?.Name ?? first.Description, kind, first.Unit,
                    g.Sum(l => l.Quantity), g.Sum(l => l.LineTotal), g.Select(l => l.SaleId).Distinct().Count());
            })
            .Where(r => type is null || (type == ProductType.Service ? r.Kind == TopProductKind.Service : r.Kind == TopProductKind.Product))
            .OrderByDescending(r => sort == TopProductSort.Amount ? r.Amount : r.Quantity)
            .ThenByDescending(r => sort == TopProductSort.Amount ? r.Quantity : r.Amount)
            .ThenBy(r => r.Name)
            .Take(Math.Clamp(take, 1, 500))
            .ToList();
        return new TopProductsResponse(from, to, sort, rows);
    }

    private static async Task<Ok<TopProductsResponse>> TopProducts(
        AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null,
        TopProductSort sort = TopProductSort.Quantity, string? type = null, int take = 50)
    {
        var (f, t) = Range(clock, from, to);
        return TypedResults.Ok(await BuildTopProductsAsync(db, f, t, sort, ParseType(type), take, ct));
    }

    private static async Task<FileContentHttpResult> TopProductsXlsx(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null,
        TopProductSort sort = TopProductSort.Quantity, string? type = null)
    {
        var (f, t) = Range(clock, from, to);
        var report = await BuildTopProductsAsync(db, f, t, sort, ParseType(type), 500, ct);
        using var book = new XLWorkbook();
        var sheet = Sheet(book, "En çok satan", sort == TopProductSort.Amount ? "En çok satan (tutara göre)" : "En çok satan (adede göre)", f, t, clock);
        Xlsx.Header(sheet, 4, "Sıra", "Ürün/hizmet", "Tür", "Miktar", "Birim", "Tutar (KDV dahil)", "Satış adedi");
        var row = 5;
        var rank = 1;
        foreach (var r in report.Rows)
        {
            Xlsx.Row(sheet, row++, rank++, r.Name, r.Kind switch { TopProductKind.Product => "Ürün", TopProductKind.Service => "Hizmet", _ => "Serbest kalem" },
                r.Quantity, CatalogUnitLabel(r.Unit), r.Amount, r.SaleCount);
        }

        Xlsx.Format(sheet, 5, row - 1, money: [6], quantities: [4]);
        return await FileAsync(db, audit, book, "en-cok-satan", f, t, ct);
    }

    /// <summary>type: "Product" veya "Service" (sorgu parametresi metin: OpenAPI'de ortak ProductType şeması null'lanmasın).</summary>
    private static ProductType? ParseType(string? type) => Enum.TryParse<ProductType>(type, ignoreCase: true, out var t) ? t : null;

    private static string CatalogUnitLabel(ProductUnit unit) => unit switch
    {
        ProductUnit.Piece => "adet",
        ProductUnit.Kg => "kg",
        ProductUnit.Liter => "lt",
        ProductUnit.Meter => "m",
        ProductUnit.Package => "paket",
        ProductUnit.Hour => "saat",
        ProductUnit.Session => "seans",
        _ => unit.ToString(),
    };

    // ---- Alacaklar ----

    internal static async Task<ReceivablesReportResponse> BuildReceivablesAsync(AppDbContext db, DateOnly today, CancellationToken ct)
    {
        var all = await ReceivableQueries.LoadAsync(db, ct);
        var rows = all.Select(r =>
        {
            decimal Bucket(int b) => r.Items.Where(i => ReceivableAllocator.AgingBucket(i.DueDate, today) == b).Sum(i => i.Remaining);
            return new ReceivableAgingRow(r.ContactId, r.Name, r.Phone, r.Items.Sum(i => i.Remaining), Bucket(0), Bucket(1), Bucket(2), Bucket(3), Bucket(4),
                r.Items.Min(i => (DateOnly?)i.DueDate));
        }).OrderByDescending(r => r.Days90Plus).ThenByDescending(r => r.Days61To90).ThenByDescending(r => r.Balance).ToList();
        var totals = new ReceivableAgingRow(Guid.Empty, "Toplam", null, rows.Sum(r => r.Balance), rows.Sum(r => r.NotDue), rows.Sum(r => r.Days0To30),
            rows.Sum(r => r.Days31To60), rows.Sum(r => r.Days61To90), rows.Sum(r => r.Days90Plus), rows.Min(r => r.OldestDueDate));
        return new ReceivablesReportResponse(today, rows, totals);
    }

    private static async Task<Ok<ReceivablesReportResponse>> Receivables(AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        TypedResults.Ok(await BuildReceivablesAsync(db, BusinessTime.Today(clock), ct));

    private static async Task<FileContentHttpResult> ReceivablesXlsx(AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var report = await BuildReceivablesAsync(db, today, ct);
        using var book = new XLWorkbook();
        var sheet = Sheet(book, "Alacaklar", $"Alacaklar ({today.ToString("dd.MM.yyyy", Tr)} itibarıyla)", null, today, clock);
        Xlsx.Header(sheet, 4, "Müşteri", "Telefon", "Toplam alacak", "Vadesi gelmemiş", "0-30 gün", "31-60 gün", "61-90 gün", "90+ gün", "En eski vade");
        var row = 5;
        foreach (var r in report.Rows.Append(report.Totals))
        {
            Xlsx.Row(sheet, row++, r.Name, r.Phone is { } p ? Customers.ContactSearchProvider.FormatPhone(p) : null, r.Balance, r.NotDue, r.Days0To30,
                r.Days31To60, r.Days61To90, r.Days90Plus, r.OldestDueDate);
        }

        sheet.Row(row - 1).Style.Font.Bold = true;
        Xlsx.Format(sheet, 5, row - 1, money: [3, 4, 5, 6, 7, 8], dates: [9]);
        return await FileAsync(db, audit, book, "alacaklar", null, today, ct);
    }

    // ---- Gelir-gider ----

    internal static async Task<IncomeExpenseResponse> BuildIncomeExpenseAsync(
        AppDbContext db, CurrentMember member, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var months = new List<string>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            months.Add(MonthKey(m));
        }

        var index = months.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        var (start, end) = BusinessTime.RangeUtc(from, to);
        var sales = await db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.Date >= start && s.Date < end)
            .Select(s => new { s.Date, s.Total, s.VatTotal }).ToListAsync(ct);
        var salesByMonth = sales.GroupBy(s => MonthKey(BusinessTime.LocalDay(s.Date))).ToDictionary(g => g.Key,
            g => (Gross: g.Sum(s => s.Total), Vat: g.Sum(s => s.VatTotal)));

        var rows = await db.CashTransactions.AsNoTracking()
            .Where(t => (t.Type == CashTransactionType.Expense || t.Type == CashTransactionType.Income) && t.Date >= from && t.Date <= to)
            .Select(t => new { t.Type, t.ExpenseCategoryId, t.Date, Net = t.IsReversal ? -t.Amount : t.Amount })
            .ToListAsync(ct);
        var categories = await db.ExpenseCategories.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking().ToListAsync(ct);
        var showSalary = FinanceVisibility.ShowSalaryDetail(member);

        IncomeExpenseLine Line(string name, bool isSales, IEnumerable<(string Month, decimal Amount)> values)
        {
            var arr = new decimal[months.Count];
            foreach (var (month, amount) in values)
            {
                if (index.TryGetValue(month, out var i))
                {
                    arr[i] += amount;
                }
            }

            return new IncomeExpenseLine(name, isSales, arr, arr.Sum());
        }

        List<IncomeExpenseLine> Lines(CashTransactionType type, ExpenseCategoryKind kind) => categories
            .Where(c => c.Kind == kind)
            .Select(c => (Category: c, Values: rows.Where(r => r.Type == type && r.ExpenseCategoryId == c.Id).Select(r => (MonthKey(r.Date), r.Net)).ToList()))
            .Where(x => x.Values.Count > 0 || x.Category.IsActive)
            .OrderBy(x => x.Category.SortOrder).ThenBy(x => x.Category.Name)
            .Select(x => Line(x.Category.IsSalary && !showSalary ? FinanceVisibility.SalaryMaskedCategory : x.Category.Name, false, x.Values))
            .ToList();

        var income = new List<IncomeExpenseLine> { Line("Satışlar", true, salesByMonth.Select(kv => (kv.Key, kv.Value.Gross))) };
        income.AddRange(Lines(CashTransactionType.Income, ExpenseCategoryKind.Income));
        var expense = Lines(CashTransactionType.Expense, ExpenseCategoryKind.Expense);

        var incomeTotals = Enumerable.Range(0, months.Count).Select(i => income.Sum(l => l.Values[i])).ToList();
        var expenseTotals = Enumerable.Range(0, months.Count).Select(i => expense.Sum(l => l.Values[i])).ToList();
        var net = incomeTotals.Zip(expenseTotals, (a, b) => a - b).ToList();
        var vat = months.Select(m => salesByMonth.TryGetValue(m, out var v) ? new SalesVatMonth(m, v.Gross - v.Vat, v.Vat, v.Gross) : new SalesVatMonth(m, 0, 0, 0))
            .ToList();
        return new IncomeExpenseResponse(from, to, months, income, expense, incomeTotals, expenseTotals, net, vat);
    }

    private static async Task<Ok<IncomeExpenseResponse>> IncomeExpense(
        CurrentMember member, AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = Range(clock, from, to);
        return TypedResults.Ok(await BuildIncomeExpenseAsync(db, member, f, t, ct));
    }

    private static async Task<FileContentHttpResult> IncomeExpenseXlsx(
        CurrentMember member, AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var (f, t) = Range(clock, from, to);
        var report = await BuildIncomeExpenseAsync(db, member, f, t, ct);
        using var book = new XLWorkbook();

        // Özet: satırlar kategori, sütunlar ay.
        var summary = Sheet(book, "Özet", "Gelir-gider", f, t, clock);
        var header = new List<string> { "Tür", "Kalem" };
        header.AddRange(report.Months.Select(MonthLabel));
        header.Add("Toplam");
        Xlsx.Header(summary, 4, [.. header]);
        var row = 5;
        void Write(string kind, IncomeExpenseLine line, bool bold = false)
        {
            var values = new List<object?> { kind, line.Name };
            values.AddRange(line.Values.Cast<object?>());
            values.Add(line.Total);
            Xlsx.Row(summary, row, [.. values]);
            if (bold)
            {
                summary.Row(row).Style.Font.Bold = true;
            }

            row++;
        }

        foreach (var line in report.Income)
        {
            Write("Gelir", line);
        }

        Write("Gelir", new IncomeExpenseLine("Gelir toplamı", false, report.IncomeTotals, report.IncomeTotals.Sum()), bold: true);
        foreach (var line in report.Expense)
        {
            Write("Gider", line);
        }

        Write("Gider", new IncomeExpenseLine("Gider toplamı", false, report.ExpenseTotals, report.ExpenseTotals.Sum()), bold: true);
        Write("", new IncomeExpenseLine("Net (gelir − gider)", false, report.Net, report.Net.Sum()), bold: true);
        Xlsx.Format(summary, 5, row - 1, money: Enumerable.Range(3, report.Months.Count + 1));

        // Ayrıntı: mali müşavir için uzun biçim; satışlarda KDV hariç, KDV ve KDV dahil.
        var detail = Sheet(book, "Ayrıntı", "Gelir-gider ayrıntısı", f, t, clock);
        Xlsx.Header(detail, 4, "Ay", "Tür", "Kalem", "KDV hariç", "KDV", "KDV dahil");
        row = 5;
        foreach (var v in report.SalesVat.Where(v => v.Gross != 0))
        {
            Xlsx.Row(detail, row++, MonthLabel(v.Month), "Gelir", "Satışlar", v.Net, v.Vat, v.Gross);
        }

        foreach (var (kind, lines) in new[] { ("Gelir", report.Income.Where(l => !l.IsSales)), ("Gider", report.Expense) })
        {
            foreach (var line in lines)
            {
                for (var i = 0; i < report.Months.Count; i++)
                {
                    if (line.Values[i] != 0)
                    {
                        Xlsx.Row(detail, row++, MonthLabel(report.Months[i]), kind, line.Name, null, null, line.Values[i]);
                    }
                }
            }
        }

        Xlsx.Format(detail, 5, row - 1, money: [4, 5, 6]);
        return await FileAsync(db, audit, book, "gelir-gider", f, t, ct);
    }

    // ---- Ortak ----

    private static IXLWorksheet Sheet(XLWorkbook book, string name, string title, DateOnly? from, DateOnly to, TimeProvider clock)
    {
        var sheet = book.AddWorksheet(name);
        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = (from is { } f ? $"{f.ToString("dd.MM.yyyy", Tr)} – {to.ToString("dd.MM.yyyy", Tr)}" : to.ToString("dd.MM.yyyy", Tr))
            + $" · Oluşturma: {BusinessTime.ToLocal(clock.GetUtcNow().UtcDateTime).ToString("dd.MM.yyyy HH:mm", Tr)}";
        return sheet;
    }

    private static async Task<FileContentHttpResult> FileAsync(
        AppDbContext db, IAuditLogger audit, XLWorkbook book, string name, DateOnly? from, DateOnly to, CancellationToken ct)
    {
        audit.Log(AuditAction.ReportExported, "Report", null, new { report = name, from, to });
        await db.SaveChangesAsync(ct);
        var range = from is { } f ? $"{f:yyyy-MM-dd}_{to:yyyy-MM-dd}" : $"{to:yyyy-MM-dd}";
        return TypedResults.File(Xlsx.Save(book), Xlsx.ContentType, $"rapor-{name}-{range}.xlsx");
    }
}
