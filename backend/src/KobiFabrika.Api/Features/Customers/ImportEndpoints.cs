using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Api.Features.Imports;
using KobiFabrika.Domain.Auditing;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Contacts;
using KobiFabrika.Domain.Finance;
using KobiFabrika.Domain.Imports;
using KobiFabrika.Infrastructure.Auditing;
using KobiFabrika.Infrastructure.Billing;
using KobiFabrika.Infrastructure.Locations;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Customers;

/// <summary>
/// Excel'den müşteri içe aktarma (/panel/musteriler/ice-aktar): şablon → yükle → sütun eşle → önizle → kaydet.
/// Yüklenen satırlar ImportJob'da 24 saat tutulur. En fazla 5000 satır; aynı telefon atlanır ve raporlanır.
/// </summary>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contacts/import").WithTags("Import").RequireAuthorization(Policies.CanWrite);

        group.MapGet("/template.xlsx", Template).WithSummary("Boş şablon (açıklama sayfasıyla)");
        group.MapPost("/", Upload).DisableAntiforgery().WithSummary("Dosyayı yükle; başlıklar ve eşleme önerisi döner");
        group.MapPost("/{jobId:guid}/preview", Preview).WithSummary("Eşlemeye göre satırları doğrula (sayfalı, status ile süzülür)");
        group.MapPost("/{jobId:guid}/commit", Commit).WithSummary("Hatasız ve tekrar olmayan satırları kaydet");
        group.MapGet("/{jobId:guid}/report.xlsx", Report).WithSummary("Kaydetme raporu (Excel)");

        return app;
    }

    private static FileContentHttpResult Template() =>
        TypedResults.File(ContactImport.Template(), LedgerEndpoints.XlsxContentType, "musteri-sablonu.xlsx");

    private static async Task<Results<Ok<ImportUploadResponse>, ProblemHttpResult>> Upload(
        IFormFile? file, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (job, headers, problem) = await ImportJobs.UploadAsync(file, ImportKind.Contacts, "musteriler.xlsx", db, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        return TypedResults.Ok(new ImportUploadResponse(
            job!.Id, job.FileName, headers!, ImportJobs.Rows(job).Count, ContactImport.SuggestMapping(headers!), ContactImport.Fields));
    }

    private sealed record Evaluated(StoredRow Row, ImportCandidate? Candidate, ImportRowResult Result);

    private static ProblemHttpResult? ValidateMapping(ImportMappingRequest request, ImportJob job)
    {
        if (ImportJobs.ValidateMapping(request, ContactImport.Fields, ImportJobs.ColumnCount(job)) is { } invalid)
        {
            return invalid;
        }

        return request.Mapping.ContainsKey("firstName") || request.Mapping.ContainsKey("companyName")
            ? null
            : Problems.BadRequest("name_not_mapped", "Ad veya Firma alanını bir sütunla eşleyin.");
    }

    /// <summary>Tüm satırları doğrular; telefon tekrarını veritabanına ve dosyadaki önceki satırlara göre işaretler.</summary>
    private static async Task<List<Evaluated>> EvaluateAsync(
        AppDbContext db, ImportJob job, Dictionary<string, int> mapping, LocationCatalog locations, bool canSetOpening, CancellationToken ct)
    {
        var rows = ImportJobs.Rows(job);
        var parsed = rows.Select(r => (Row: r, Parse: ContactImport.Parse(r.RowNumber, r.Cells, mapping, locations, canSetOpening))).ToList();

        var phones = parsed.Select(p => p.Parse.Candidate?.Phone).OfType<string>().Distinct().ToList();
        var existing = new Dictionary<string, string>();
        foreach (var chunk in phones.Chunk(1000))
        {
            foreach (var c in await db.Contacts.AsNoTracking().Where(c => c.Phone != null && chunk.Contains(c.Phone))
                         .Select(c => new { c.Phone, c.DisplayName }).ToListAsync(ct))
            {
                existing[c.Phone!] = c.DisplayName;
            }
        }

        var seen = new Dictionary<string, int>();
        var result = new List<Evaluated>();
        foreach (var (row, (candidate, errors, warnings)) in parsed)
        {
            var name = candidate is not null ? ContactImport.DisplayName(candidate) : ContactImport.DisplayName(row.Cells, mapping);
            var phone = candidate?.Phone;
            if (candidate is null)
            {
                result.Add(new Evaluated(row, null, new ImportRowResult(row.RowNumber, name, phone, ImportRowStatus.Error, errors)));
            }
            else if (phone is not null && existing.TryGetValue(phone, out var owner))
            {
                result.Add(new Evaluated(row, null, new ImportRowResult(row.RowNumber, name, phone, ImportRowStatus.Duplicate,
                    [$"Bu numara {owner} adına kayıtlı; atlanacak."])));
            }
            else if (phone is not null && seen.TryGetValue(phone, out var firstRow))
            {
                result.Add(new Evaluated(row, null, new ImportRowResult(row.RowNumber, name, phone, ImportRowStatus.Duplicate,
                    [$"Dosyada {firstRow}. satırla aynı numara; atlanacak."])));
            }
            else
            {
                if (phone is not null)
                {
                    seen[phone] = row.RowNumber;
                }

                result.Add(new Evaluated(row, candidate, new ImportRowResult(row.RowNumber, name, phone, ImportRowStatus.Ok, warnings)));
            }
        }

        return result;
    }

    private static async Task<Results<Ok<ImportPreviewResponse>, ProblemHttpResult>> Preview(
        Guid jobId, ImportMappingRequest request, CurrentMember member, AppDbContext db, LocationCatalog locations, TimeProvider clock,
        CancellationToken ct, ImportRowStatus? status = null, int page = 1, int pageSize = 50)
    {
        var (job, problem) = await ImportJobs.LoadAsync(db, jobId, ImportKind.Contacts, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ValidateMapping(request, job!) is { } invalid)
        {
            return invalid;
        }

        var rows = await EvaluateAsync(db, job!, request.Mapping, locations, member.CanManageFinance, ct);
        return TypedResults.Ok(ImportJobs.Preview(rows.Select(r => r.Result).ToList(), status, page, pageSize));
    }

    private static async Task<Results<Ok<ImportCommitResponse>, ProblemHttpResult>> Commit(
        Guid jobId, ImportMappingRequest request, CurrentMember member, AppDbContext db, LedgerService ledger, LocationCatalog locations,
        IAuditLogger audit, IPlanLimits limits, TimeProvider clock, CancellationToken ct)
    {
        var (job, problem) = await ImportJobs.LoadAsync(db, jobId, ImportKind.Contacts, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (job!.Status == ImportJobStatus.Completed)
        {
            return Problems.Conflict("import_completed", "Bu dosya zaten içe aktarıldı.");
        }

        if (ValidateMapping(request, job) is { } invalid)
        {
            return invalid;
        }

        var rows = await EvaluateAsync(db, job, request.Mapping, locations, member.CanManageFinance, ct);
        var toAdd = rows.Where(r => r.Candidate is not null).ToList();
        if (!await limits.CanAddContactsAsync(toAdd.Count, ct))
        {
            return Problems.Forbidden("plan_limit", "Paketinizin müşteri sınırı bu kadar kaydı almıyor.");
        }

        var results = rows.Select(r => r.Result).ToList();
        var report = ImportJobs.CommitReport(results);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = BusinessTime.Today(clock);
        await ledger.InTransactionAsync(async () =>
        {
            // Etiketler: olmayanlar oluşturulur (büyük/küçük harf duyarsız eşleşme).
            var tagNames = toAdd.SelectMany(r => r.Candidate!.Tags).Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
            var tags = await db.Tags.ToListAsync(ct);
            var tagByName = new Dictionary<string, Guid>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var t in tags)
            {
                tagByName.TryAdd(t.Name, t.Id);
            }

            foreach (var name in tagNames.Where(n => !tagByName.ContainsKey(n)))
            {
                var tag = new Tag { Name = name };
                db.Tags.Add(tag);
                tagByName[name] = tag.Id;
            }

            var pending = 0;
            foreach (var row in toAdd)
            {
                var c = row.Candidate!;
                var contact = new Contact
                {
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    CompanyName = c.CompanyName,
                    Phone = c.Phone,
                    Email = c.Email,
                    BirthDate = c.BirthDate,
                    Address = c.Address,
                    City = c.City,
                    District = c.District,
                    Source = c.Source,
                    Notes = c.Notes,
                    // Yeni kayıt: açılış bakiyesi hareketle birlikte aynı transaction'da yazılır (başka işlem bu satırı göremez).
                    Balance = c.OpeningBalance ?? 0,
                };
                contact.RefreshDisplayName();
                db.Contacts.Add(contact);
                ContactEndpoints.ApplyConsents(db, contact, new ConsentsRequest(c.Sms, c.EmailConsent, c.Call), ConsentSource.Import, now);
                foreach (var tagName in c.Tags)
                {
                    db.ContactTags.Add(new ContactTag { ContactId = contact.Id, TagId = tagByName[tagName] });
                }

                if (c.OpeningBalance is { } opening)
                {
                    db.LedgerEntries.Add(ContactEndpoints.OpeningEntry(
                        contact.Id, Math.Abs(opening), opening > 0 ? BalanceDirection.Receivable : BalanceDirection.Payable, today));
                }

                if (++pending == 500)
                {
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                    pending = 0;
                }
            }

            await ImportJobs.CompleteAsync(db, jobId, results, ct);
            audit.Log(AuditAction.ContactsImported, nameof(ImportJob), jobId,
                new { file = job.FileName, added = report.Added, duplicates = report.Duplicates, errors = report.Errors });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return TypedResults.Ok(report);
    }

    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> Report(Guid jobId, AppDbContext db, CancellationToken ct) =>
        ImportJobs.ReportAsync(db, jobId, ImportKind.Contacts, "Telefon", ct);
}
