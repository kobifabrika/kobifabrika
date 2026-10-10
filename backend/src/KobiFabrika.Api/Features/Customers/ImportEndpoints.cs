using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Api.Features.Finance;
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

public sealed record ImportUploadResponse(
    Guid JobId, string FileName, IReadOnlyList<string> Headers, int RowCount,
    IReadOnlyDictionary<string, int> Mapping, IReadOnlyList<ImportFieldInfo> Fields);

/// <summary>Alan anahtarı → sütun sırası (0'dan başlar). Eşlenmeyen alan yazılmaz.</summary>
public sealed record ImportMappingRequest(Dictionary<string, int> Mapping);

public sealed record ImportPreviewResponse(int Total, int Ok, int Errors, int Duplicates, IReadOnlyList<ImportRowResult> Rows);

public sealed record ImportCommitResponse(int Added, int Duplicates, int Errors, IReadOnlyList<ImportRowResult> NotImported, int Warnings);

/// <summary>
/// Excel'den müşteri içe aktarma (/panel/musteriler/ice-aktar): şablon → yükle → sütun eşle → önizle → kaydet.
/// Yüklenen satırlar ImportJob'da 24 saat tutulur. En fazla 5000 satır; aynı telefon atlanır ve raporlanır.
/// </summary>
public static class ImportEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

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
        if (file is null || file.Length == 0)
        {
            return Problems.BadRequest("file_required", "Bir Excel dosyası (.xlsx) seçin.");
        }

        if (file.Length > ContactImport.MaxFileBytes)
        {
            return Problems.BadRequest("file_too_large", "Dosya en fazla 5 MB olabilir.");
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        stream.Position = 0;

        // .xlsx bir ZIP'tir: "PK\x03\x04" ile başlar.
        var head = new byte[4];
        if (await stream.ReadAsync(head, ct) < 4 || head[0] != 'P' || head[1] != 'K' || head[2] != 3 || head[3] != 4)
        {
            return Problems.BadRequest("invalid_file", "Dosya Excel (.xlsx) biçiminde değil. Şablonu indirip onunla deneyin.");
        }

        stream.Position = 0;
        List<string> headers;
        List<(int RowNumber, string?[] Cells)> rows;
        try
        {
            (headers, rows) = ContactImport.Read(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problems.BadRequest("invalid_file", "Dosya okunamadı. Şablonu indirip onunla deneyin.");
        }

        if (headers.Count == 0 || rows.Count == 0)
        {
            return Problems.BadRequest("empty_file", "Dosyada başlık satırının altında veri yok.");
        }

        if (rows.Count > ImportJob.MaxRows)
        {
            return Problems.BadRequest("too_many_rows", $"En fazla {ImportJob.MaxRows} satır aktarılabilir; dosyada {rows.Count} satır var.");
        }

        var job = new ImportJob
        {
            FileName = Path.GetFileName(file.FileName) is { Length: > 0 and <= 200 } name ? name : "musteriler.xlsx",
            Status = ImportJobStatus.Uploaded,
            Headers = JsonSerializer.Serialize(headers, Json),
            Rows = JsonSerializer.Serialize(rows.Select(r => new StoredRow(r.RowNumber, r.Cells)), Json),
            ExpiresAt = clock.GetUtcNow().UtcDateTime + ImportJob.Lifetime,
        };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ImportUploadResponse(job.Id, job.FileName, headers, rows.Count, ContactImport.SuggestMapping(headers), ContactImport.Fields));
    }

    private sealed record StoredRow(int RowNumber, string?[] Cells);

    private sealed record Evaluated(StoredRow Row, ImportCandidate? Candidate, ImportRowResult Result);

    private static async Task<(ImportJob? Job, ProblemHttpResult? Problem)> LoadJobAsync(AppDbContext db, Guid jobId, TimeProvider clock, CancellationToken ct)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
        {
            return (null, Problems.NotFound("İçe aktarma bulunamadı veya süresi doldu. Dosyayı tekrar yükleyin."));
        }

        return (job, null);
    }

    private static ProblemHttpResult? ValidateMapping(ImportMappingRequest request, int columnCount)
    {
        var keys = ContactImport.Fields.Select(f => f.Key).ToHashSet();
        var mapping = request.Mapping ?? [];
        if (mapping.Keys.Any(k => !keys.Contains(k)) || mapping.Values.Any(v => v < 0 || v >= columnCount)
            || mapping.Values.Distinct().Count() != mapping.Count)
        {
            return Problems.BadRequest("invalid_mapping", "Sütun eşlemesi geçersiz. Her sütunu en fazla bir alana eşleyin.");
        }

        if (!mapping.ContainsKey("firstName") && !mapping.ContainsKey("companyName"))
        {
            return Problems.BadRequest("name_not_mapped", "Ad veya Firma alanını bir sütunla eşleyin.");
        }

        return null;
    }

    /// <summary>Tüm satırları doğrular; telefon tekrarını veritabanına ve dosyadaki önceki satırlara göre işaretler.</summary>
    private static async Task<List<Evaluated>> EvaluateAsync(
        AppDbContext db, ImportJob job, Dictionary<string, int> mapping, LocationCatalog locations, bool canSetOpening, CancellationToken ct)
    {
        var rows = JsonSerializer.Deserialize<List<StoredRow>>(job.Rows, Json)!;
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
        var (job, problem) = await LoadJobAsync(db, jobId, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ValidateMapping(request, JsonSerializer.Deserialize<List<string>>(job!.Headers, Json)!.Count) is { } invalid)
        {
            return invalid;
        }

        var rows = await EvaluateAsync(db, job, request.Mapping, locations, member.CanManageFinance, ct);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var filtered = rows.Select(r => r.Result).Where(r => status is null || r.Status == status)
            .OrderBy(r => r.Status == ImportRowStatus.Ok).ThenBy(r => r.RowNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return TypedResults.Ok(new ImportPreviewResponse(
            rows.Count,
            rows.Count(r => r.Result.Status == ImportRowStatus.Ok),
            rows.Count(r => r.Result.Status == ImportRowStatus.Error),
            rows.Count(r => r.Result.Status == ImportRowStatus.Duplicate),
            filtered));
    }

    private static async Task<Results<Ok<ImportCommitResponse>, ProblemHttpResult>> Commit(
        Guid jobId, ImportMappingRequest request, CurrentMember member, AppDbContext db, LedgerService ledger, LocationCatalog locations,
        IAuditLogger audit, IPlanLimits limits, TimeProvider clock, CancellationToken ct)
    {
        var (job, problem) = await LoadJobAsync(db, jobId, clock, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (job!.Status == ImportJobStatus.Completed)
        {
            return Problems.Conflict("import_completed", "Bu dosya zaten içe aktarıldı.");
        }

        if (ValidateMapping(request, JsonSerializer.Deserialize<List<string>>(job.Headers, Json)!.Count) is { } invalid)
        {
            return invalid;
        }

        var rows = await EvaluateAsync(db, job, request.Mapping, locations, member.CanManageFinance, ct);
        var toAdd = rows.Where(r => r.Candidate is not null).ToList();
        if (!await limits.CanAddContactsAsync(toAdd.Count, ct))
        {
            return Problems.Forbidden("plan_limit", "Paketinizin müşteri sınırı bu kadar kaydı almıyor.");
        }

        var report = new ImportCommitResponse(
            toAdd.Count,
            rows.Count(r => r.Result.Status == ImportRowStatus.Duplicate),
            rows.Count(r => r.Result.Status == ImportRowStatus.Error),
            rows.Where(r => r.Result.Status != ImportRowStatus.Ok).Select(r => r.Result).ToList(),
            rows.Count(r => r.Result.Status == ImportRowStatus.Ok && r.Result.Messages.Count > 0));
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

            var tracked = await db.ImportJobs.FirstAsync(j => j.Id == jobId, ct);
            tracked.Status = ImportJobStatus.Completed;
            tracked.Result = JsonSerializer.Serialize(rows.Select(r => r.Result), Json);
            audit.Log(AuditAction.ContactsImported, nameof(ImportJob), jobId,
                new { file = job.FileName, added = report.Added, duplicates = report.Duplicates, errors = report.Errors });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return TypedResults.Ok(report);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Report(Guid jobId, AppDbContext db, CancellationToken ct)
    {
        var job = await db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job?.Result is null)
        {
            return Problems.NotFound("Rapor bulunamadı.");
        }

        var rows = JsonSerializer.Deserialize<List<ImportRowResult>>(job.Result, Json)!;
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Rapor");
        LedgerEndpoints.Header(sheet, 1, "Satır", "Ad", "Telefon", "Durum", "Açıklama");
        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.RowNumber;
            sheet.Cell(r, 2).Value = row.Name;
            sheet.Cell(r, 3).Value = row.Phone;
            sheet.Cell(r, 4).Value = row.Status switch
            {
                ImportRowStatus.Ok => "Eklendi",
                ImportRowStatus.Duplicate => "Atlandı (tekrar)",
                _ => "Hatalı",
            };
            sheet.Cell(r, 5).Value = string.Join(" ", row.Messages);
            r++;
        }

        sheet.Columns().AdjustToContents();
        return TypedResults.File(LedgerEndpoints.Save(book), LedgerEndpoints.XlsxContentType, "ice-aktarma-raporu.xlsx");
    }
}
