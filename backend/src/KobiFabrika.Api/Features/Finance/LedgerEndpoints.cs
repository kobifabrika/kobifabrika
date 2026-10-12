using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Pdf;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Finance;

/// <summary>
/// Cari hareketler, açılış bakiyesi, tahsilat, kasa hesapları, PDF ekstre ve Excel. Görme: Sahip, Yönetici, Mali Müşavir;
/// yazma: Sahip, Yönetici. Bakiye = Σ(Borç − Alacak); pozitif "Alacak" (müşteri bize borçlu), negatif "Borç".
/// </summary>
public static class LedgerEndpoints
{
    public const string XlsxContentType = Xlsx.ContentType;

    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var contacts = app.MapGroup("/api/contacts/{id:guid}").WithTags("Ledger");
        contacts.MapGet("/ledger", Ledger).RequireAuthorization(Policies.ViewFinance).WithSummary("Cari hareketler (from/to; devreden ve yürüyen bakiye)");
        contacts.MapPost("/ledger", AddEntry).RequireAuthorization(Policies.ManageFinance).Validate<LedgerEntryRequest>()
            .WithSummary("Elle borç/alacak kaydı");
        contacts.MapPut("/opening-balance", SetOpening).RequireAuthorization(Policies.ManageFinance).Validate<OpeningBalanceRequest>()
            .WithSummary("Açılış bakiyesini gir/değiştir");
        contacts.MapDelete("/opening-balance", DeleteOpening).RequireAuthorization(Policies.ManageFinance).WithSummary("Açılış bakiyesini kaldır");
        contacts.MapPost("/collections", Collect).RequireAuthorization(Policies.ManageFinance).Validate<CollectionRequest>()
            .WithSummary("Tahsilat al (kasaya/bankaya giriş + cariye alacak)");
        contacts.MapGet("/statement.pdf", StatementPdf).RequireAuthorization(Policies.ViewFinance).WithSummary("PDF ekstre");
        contacts.MapGet("/ledger.xlsx", LedgerXlsx).RequireAuthorization(Policies.Export).WithSummary("Cari hareketler Excel");

        app.MapDelete("/api/ledger-entries/{entryId:guid}", DeleteEntry).WithTags("Ledger").RequireAuthorization(Policies.ManageFinance)
            .WithSummary("Elle girilen hareketi sil (tahsilat kaydı 'Tahsilatı geri al' ile)");
        app.MapGet("/api/contacts/export.xlsx", ExportContacts).WithTags("Contacts").RequireAuthorization(Policies.Export)
            .WithSummary("Müşteri/tedarikçi listesini Excel'e aktar (liste filtreleriyle)");

        return app;
    }

    private static Task<Contact?> FindContactAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    private static async Task<Results<Ok<LedgerResponse>, ProblemHttpResult>> Ledger(
        Guid id, AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        if (await FindContactAsync(db, id, ct) is not { } contact)
        {
            return ContactEndpoints.ContactNotFound();
        }

        return TypedResults.Ok(await LedgerQueries.BuildAsync(db, contact, from, to ?? BusinessTime.Today(clock), ct));
    }

    private static async Task<Results<Ok<LedgerResponse>, ProblemHttpResult>> AddEntry(
        Guid id, LedgerEntryRequest request, AppDbContext db, LedgerService ledger, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        if (await FindContactAsync(db, id, ct) is null)
        {
            return ContactEndpoints.ContactNotFound();
        }

        var amount = request.Amount!.Value;
        var entry = new LedgerEntry
        {
            ContactId = id,
            Date = request.Date ?? BusinessTime.Today(clock),
            DueDate = request.DueDate,
            Debit = request.Kind == LedgerKind.Debit ? amount : 0,
            Credit = request.Kind == LedgerKind.Credit ? amount : 0,
            Description = request.Description!.Trim(),
            RefType = LedgerRefType.Manual,
        };

        await ledger.InTransactionAsync(async () =>
        {
            audit.Log(AuditAction.LedgerEntryCreated, nameof(LedgerEntry), entry.Id,
                new { contactId = id, kind = request.Kind.ToString(), amount, entry.Description });
            await ledger.AddEntryAsync(entry, ct);
            return true;
        }, ct);

        var contact = (await FindContactAsync(db, id, ct))!;
        return TypedResults.Ok(await LedgerQueries.BuildAsync(db, contact, null, BusinessTime.Today(clock), ct));
    }

    private static async Task<Results<Ok<LedgerResponse>, ProblemHttpResult>> SetOpening(
        Guid id, OpeningBalanceRequest request, AppDbContext db, LedgerService ledger, IAuditLogger audit, TimeProvider clock, CancellationToken ct)
    {
        if (await FindContactAsync(db, id, ct) is null)
        {
            return ContactEndpoints.ContactNotFound();
        }

        await ledger.InTransactionAsync(async () =>
        {
            if (await db.LedgerEntries.FirstOrDefaultAsync(e => e.ContactId == id && e.RefType == LedgerRefType.Opening, ct) is { } old)
            {
                await ledger.RemoveEntryAsync(old, ct);
            }

            audit.Log(AuditAction.OpeningBalanceSet, nameof(Contact), id, new { amount = request.Amount, direction = request.Direction.ToString() });
            await ledger.AddEntryAsync(
                ContactEndpoints.OpeningEntry(id, request.Amount!.Value, request.Direction, request.Date ?? BusinessTime.Today(clock)), ct);
            return true;
        }, ct);

        var contact = (await FindContactAsync(db, id, ct))!;
        return TypedResults.Ok(await LedgerQueries.BuildAsync(db, contact, null, BusinessTime.Today(clock), ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteOpening(
        Guid id, AppDbContext db, LedgerService ledger, IAuditLogger audit, CancellationToken ct)
    {
        var entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.ContactId == id && e.RefType == LedgerRefType.Opening, ct);
        if (entry is null)
        {
            return Problems.NotFound("Açılış bakiyesi yok.");
        }

        await ledger.InTransactionAsync(async () =>
        {
            audit.Log(AuditAction.LedgerEntryDeleted, nameof(LedgerEntry), entry.Id, new { contactId = id, opening = true });
            await ledger.RemoveEntryAsync(entry, ct);
            return true;
        }, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEntry(
        Guid entryId, AppDbContext db, LedgerService ledger, IAuditLogger audit, CancellationToken ct)
    {
        var entry = await db.LedgerEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry is null)
        {
            return Problems.NotFound("Hareket bulunamadı.");
        }

        if (entry.RefType is not (LedgerRefType.Manual or LedgerRefType.Opening))
        {
            return Problems.Conflict("not_manual_entry", "Bu hareket tahsilat, ödeme veya satıştan geliyor; kasa hareketinden ters kayıtla geri alın.");
        }

        await ledger.InTransactionAsync(async () =>
        {
            audit.Log(AuditAction.LedgerEntryDeleted, nameof(LedgerEntry), entry.Id,
                new { entry.ContactId, entry.Debit, entry.Credit, entry.Description });
            await ledger.RemoveEntryAsync(entry, ct);
            return true;
        }, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<CollectionResponse>, ProblemHttpResult, ValidationProblem>> Collect(
        Guid id, CollectionRequest request, AppDbContext db, LedgerService ledger, CollectionService collections, IAuditLogger audit,
        TimeProvider clock, CancellationToken ct)
    {
        if (await FindContactAsync(db, id, ct) is not { } contact)
        {
            return ContactEndpoints.ContactNotFound();
        }

        if (await db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.CashAccountId && a.IsActive, ct) is not { } account)
        {
            return (ValidationProblem)Validation.FieldProblem("cashAccountId", "Kasa veya banka hesabı bulunamadı.");
        }

        var amount = request.Amount!.Value;
        var description = string.IsNullOrWhiteSpace(request.Description) ? CollectionService.DefaultDescription(account) : request.Description.Trim();
        var (cash, entry) = await ledger.InTransactionAsync(async () =>
        {
            var result = await collections.RecordAsync(contact, account, amount, request.Date ?? BusinessTime.Today(clock), description, null, ct);
            audit.Log(AuditAction.CollectionRecorded, nameof(CashTransaction), result.Cash.Id,
                new { contactId = id, amount, cashAccount = account.Name });
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);

        var balance = await db.Contacts.Where(c => c.Id == id).Select(c => c.Balance).FirstAsync(ct);
        return TypedResults.Created($"/api/cash-transactions/{cash.Id}",
            new CollectionResponse(cash.Id, entry.Id, balance, await ledger.CashBalanceAsync(account.Id, ct)));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> StatementPdf(
        Guid id, AppDbContext db, IFileStorage storage, IPdfRenderer pdf, TimeProvider clock, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (await FindContactAsync(db, id, ct) is not { } contact)
        {
            return ContactEndpoints.ContactNotFound();
        }

        var data = await LedgerQueries.StatementAsync(db, storage, clock, contact, from, to ?? BusinessTime.Today(clock), ct);
        return TypedResults.File(pdf.Statement(data), "application/pdf", $"ekstre-{LedgerQueries.FileSafe(contact.DisplayName)}.pdf");
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> LedgerXlsx(
        Guid id, AppDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        if (await FindContactAsync(db, id, ct) is not { } contact)
        {
            return ContactEndpoints.ContactNotFound();
        }

        var ledger = await LedgerQueries.BuildAsync(db, contact, from, to ?? BusinessTime.Today(clock), ct);
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Cari hareketler");
        sheet.Cell(1, 1).Value = contact.DisplayName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        Header(sheet, 3, "Tarih", "Açıklama", "Vade", "Borç", "Alacak", "Bakiye");
        var row = 4;
        if (from is not null)
        {
            sheet.Cell(row, 1).Value = from.Value.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 2).Value = "Devreden bakiye";
            sheet.Cell(row, 6).Value = ledger.CarriedBalance;
            row++;
        }

        foreach (var item in ledger.Items)
        {
            sheet.Cell(row, 1).Value = item.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 2).Value = item.Description;
            if (item.DueDate is { } due)
            {
                sheet.Cell(row, 3).Value = due.ToDateTime(TimeOnly.MinValue);
            }

            sheet.Cell(row, 4).Value = item.Debit;
            sheet.Cell(row, 5).Value = item.Credit;
            sheet.Cell(row, 6).Value = item.Balance;
            row++;
        }

        sheet.Range(4, 1, Math.Max(row, 4), 1).Style.DateFormat.Format = "dd.MM.yyyy";
        sheet.Range(4, 3, Math.Max(row, 4), 3).Style.DateFormat.Format = "dd.MM.yyyy";
        sheet.Range(4, 4, Math.Max(row, 4), 6).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();
        return TypedResults.File(Save(book), XlsxContentType, $"cari-{LedgerQueries.FileSafe(contact.DisplayName)}.xlsx");
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportContacts(
        AppDbContext db, IAuditLogger audit, TimeProvider clock, CancellationToken ct,
        string? search = null, string? type = null, bool archived = false)
    {
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.IsArchived == archived)
            .Apply(new ContactFilter(type), db, clock.GetUtcNow().UtcDateTime)
            .Search(search)
            .Sort(null)
            .ToListAsync(ct);
        var tags = await ContactQueries.TagsForAsync(db, contacts.Select(c => c.Id).ToList(), ct);

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Müşteriler");
        Header(sheet, 1, "Ad", "Soyad", "Firma", "Telefon", "E-posta", "Doğum tarihi", "Adres", "İl", "İlçe", "Kaynak", "Etiketler",
            "Müşteri", "Tedarikçi", "SMS izni", "E-posta izni", "Arama izni", "Bakiye", "Bakiye türü", "Vergi no", "Vergi dairesi", "Kayıt tarihi");
        var row = 2;
        foreach (var c in contacts)
        {
            object?[] values =
            [
                c.FirstName, c.LastName, c.CompanyName, c.Phone is { } p ? ContactSearchProvider.FormatPhone(p) : null, c.Email,
                c.BirthDate?.ToDateTime(TimeOnly.MinValue), c.Address, c.City, c.District,
                c.Source is { } s ? ContactLabels.Sources[s] : null,
                string.Join(", ", (tags.GetValueOrDefault(c.Id) ?? []).Select(t => t.Name)),
                c.IsCustomer ? "Evet" : "Hayır", c.IsSupplier ? "Evet" : "Hayır",
                c.SmsConsent ? "Evet" : "Hayır", c.EmailConsent ? "Evet" : "Hayır", c.CallConsent ? "Evet" : "Hayır",
                Math.Abs(c.Balance), c.Balance switch { > 0 => "Alacak", < 0 => "Borç", _ => "" },
                c.TaxNumber, c.TaxOffice, BusinessTime.ToLocal(c.CreatedAt),
            ];
            for (var i = 0; i < values.Length; i++)
            {
                sheet.Cell(row, i + 1).Value = XLCellValue.FromObject(values[i]);
            }

            row++;
        }

        sheet.Column(6).Style.DateFormat.Format = "dd.MM.yyyy";
        sheet.Column(17).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(21).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
        sheet.Columns().AdjustToContents();

        audit.Log(AuditAction.ContactsExported, nameof(Contact), null, new { count = contacts.Count });
        await db.SaveChangesAsync(ct);
        var today = BusinessTime.Today(clock);
        return TypedResults.File(Save(book), XlsxContentType, $"musteriler-{today:yyyy-MM-dd}.xlsx");
    }

    internal static void Header(IXLWorksheet sheet, int row, params string[] titles) => Xlsx.Header(sheet, row, titles);

    internal static byte[] Save(XLWorkbook book) => Xlsx.Save(book);
}
