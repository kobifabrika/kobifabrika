using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Imports;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Imports;

public enum ImportRowStatus
{
    Ok,
    Error,
    Duplicate,
}

public sealed record ImportFieldInfo(string Key, string Label);

/// <summary>
/// Satır sonucu. Detail: müşteride telefon, üründe barkod. Messages: hata veya atlanma nedeni, ya da (Ok için) uyarı.
/// </summary>
public sealed record ImportRowResult(int RowNumber, string? Name, string? Detail, ImportRowStatus Status, IReadOnlyList<string> Messages);

public sealed record ImportUploadResponse(
    Guid JobId, string FileName, IReadOnlyList<string> Headers, int RowCount,
    IReadOnlyDictionary<string, int> Mapping, IReadOnlyList<ImportFieldInfo> Fields);

/// <summary>Alan anahtarı → sütun sırası (0'dan başlar). Eşlenmeyen alan yazılmaz.</summary>
public sealed record ImportMappingRequest(Dictionary<string, int> Mapping);

public sealed record ImportPreviewResponse(int Total, int Ok, int Errors, int Duplicates, IReadOnlyList<ImportRowResult> Rows);

public sealed record ImportCommitResponse(int Added, int Duplicates, int Errors, IReadOnlyList<ImportRowResult> NotImported, int Warnings);

/// <summary>Yüklenen ham satır (ImportJob.Rows JSON'unda).</summary>
public sealed record StoredRow(int RowNumber, string?[] Cells);

/// <summary>
/// Excel içe aktarmalarının (müşteri, ürün) ortak adımları: dosyayı doğrulayıp okumak, oturumu (ImportJob) 24 saat saklamak,
/// başlıklardan eşleme önermek, önizlemeyi sayfalamak ve kaydetme raporunu Excel olarak vermek. Oturum türüne bağlıdır:
/// müşteri oturumu ürün uçlarında kullanılamaz.
/// </summary>
public static class ImportJobs
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Küçük harf, Türkçe karaktersiz, tek boşluklu (başlık ve değer eşleştirmesi için).</summary>
    public static string Simplify(string text)
    {
        var lower = text.Trim().ToLower(Tr)
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var sb = new StringBuilder();
        foreach (var ch in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return string.Join(' ', sb.ToString().Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Başlıklara göre alan → sütun önerisi (bulunamayan alan eşlenmez). Eş anlamlılar sadeleştirilmiş yazılır.</summary>
    public static Dictionary<string, int> SuggestMapping(
        IReadOnlyList<string> headers, IReadOnlyList<ImportFieldInfo> fields, IReadOnlyDictionary<string, string[]> aliases)
    {
        var mapping = new Dictionary<string, int>();
        var simplified = headers.Select(Simplify).ToList();
        foreach (var field in fields)
        {
            var label = Simplify(field.Label);
            var index = simplified.FindIndex(h => h == label || aliases[field.Key].Contains(h));
            if (index >= 0 && !mapping.ContainsValue(index))
            {
                mapping[field.Key] = index;
            }
        }

        return mapping;
    }

    /// <summary>Eşleme geçerli mi: bilinen alanlar, var olan sütunlar, bir sütun en fazla bir alana.</summary>
    public static ProblemHttpResult? ValidateMapping(ImportMappingRequest request, IReadOnlyList<ImportFieldInfo> fields, int columnCount)
    {
        var keys = fields.Select(f => f.Key).ToHashSet();
        var mapping = request.Mapping ?? [];
        if (mapping.Keys.Any(k => !keys.Contains(k)) || mapping.Values.Any(v => v < 0 || v >= columnCount)
            || mapping.Values.Distinct().Count() != mapping.Count)
        {
            return Problems.BadRequest("invalid_mapping", "Sütun eşlemesi geçersiz. Her sütunu en fazla bir alana eşleyin.");
        }

        return null;
    }

    /// <summary>Şablon: ilk sayfa başlıklar (boş), "Açıklama" sayfası alanların nasıl doldurulacağı.</summary>
    public static byte[] Template(string sheetName, IReadOnlyList<ImportFieldInfo> fields, IEnumerable<int> textColumns, string[][] help)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(sheetName);
        for (var i = 0; i < fields.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = fields[i].Label;
            cell.Style.Font.Bold = true;
        }

        foreach (var column in textColumns)
        {
            sheet.Column(column).Style.NumberFormat.Format = "@";
        }

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        var helpSheet = book.AddWorksheet("Açıklama");
        for (var r = 0; r < help.Length; r++)
        {
            for (var c = 0; c < help[r].Length; c++)
            {
                helpSheet.Cell(r + 1, c + 1).Value = help[r][c];
            }
        }

        helpSheet.Row(1).Style.Font.Bold = true;
        helpSheet.Columns().AdjustToContents();
        return LedgerEndpoints.Save(book);
    }

    /// <summary>Dosyayı doğrular, okur ve oturumu kaydeder. Başarılıysa (job, headers), değilse problem.</summary>
    public static async Task<(ImportJob? Job, List<string>? Headers, ProblemHttpResult? Problem)> UploadAsync(
        IFormFile? file, ImportKind kind, string defaultFileName, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return (null, null, Problems.BadRequest("file_required", "Bir Excel dosyası (.xlsx) seçin."));
        }

        if (file.Length > MaxFileBytes)
        {
            return (null, null, Problems.BadRequest("file_too_large", "Dosya en fazla 5 MB olabilir."));
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        stream.Position = 0;

        // .xlsx bir ZIP'tir: "PK\x03\x04" ile başlar.
        var head = new byte[4];
        if (await stream.ReadAsync(head, ct) < 4 || head[0] != 'P' || head[1] != 'K' || head[2] != 3 || head[3] != 4)
        {
            return (null, null, Problems.BadRequest("invalid_file", "Dosya Excel (.xlsx) biçiminde değil. Şablonu indirip onunla deneyin."));
        }

        stream.Position = 0;
        List<string> headers;
        List<StoredRow> rows;
        try
        {
            (headers, rows) = Read(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, null, Problems.BadRequest("invalid_file", "Dosya okunamadı. Şablonu indirip onunla deneyin."));
        }

        if (headers.Count == 0 || rows.Count == 0)
        {
            return (null, null, Problems.BadRequest("empty_file", "Dosyada başlık satırının altında veri yok."));
        }

        if (rows.Count > ImportJob.MaxRows)
        {
            return (null, null, Problems.BadRequest("too_many_rows", $"En fazla {ImportJob.MaxRows} satır aktarılabilir; dosyada {rows.Count} satır var."));
        }

        var job = new ImportJob
        {
            Kind = kind,
            FileName = Path.GetFileName(file.FileName) is { Length: > 0 and <= 200 } name ? name : defaultFileName,
            Status = ImportJobStatus.Uploaded,
            Headers = JsonSerializer.Serialize(headers, Json),
            Rows = JsonSerializer.Serialize(rows, Json),
            ExpiresAt = clock.GetUtcNow().UtcDateTime + ImportJob.Lifetime,
        };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return (job, headers, null);
    }

    /// <summary>İlk sayfanın ilk satırı başlık; sonraki boş olmayan satırlar veri. Hücreler metne çevrilir (tarih → yyyy-MM-dd).</summary>
    public static (List<string> Headers, List<StoredRow> Rows) Read(Stream stream)
    {
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.First();
        var range = sheet.RangeUsed();
        if (range is null)
        {
            return ([], []);
        }

        var lastColumn = range.LastColumn().ColumnNumber();
        var headerRow = range.FirstRow().RowNumber();
        var headers = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(headerRow, c).GetFormattedString().Trim()).ToList();

        var rows = new List<StoredRow>();
        for (var r = headerRow + 1; r <= range.LastRow().RowNumber(); r++)
        {
            var cells = Enumerable.Range(1, lastColumn).Select(c => CellText(sheet.Cell(r, c))).ToArray();
            if (cells.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                rows.Add(new StoredRow(r, cells));
            }
        }

        return (headers, rows);
    }

    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => ((decimal)value.GetNumber()).ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "Evet" : "Hayır",
            _ => cell.GetFormattedString().Trim(),
        };
    }

    /// <summary>Oturumu türüne göre yükler; yoksa, süresi dolmuşsa veya başka türdeyse 404.</summary>
    public static async Task<(ImportJob? Job, ProblemHttpResult? Problem)> LoadAsync(
        AppDbContext db, Guid jobId, ImportKind kind, TimeProvider clock, CancellationToken ct)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.Kind == kind, ct);
        if (job is null || job.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
        {
            return (null, Problems.NotFound("İçe aktarma bulunamadı veya süresi doldu. Dosyayı tekrar yükleyin."));
        }

        return (job, null);
    }

    public static int ColumnCount(ImportJob job) => JsonSerializer.Deserialize<List<string>>(job.Headers, Json)!.Count;

    public static List<StoredRow> Rows(ImportJob job) => JsonSerializer.Deserialize<List<StoredRow>>(job.Rows, Json)!;

    /// <summary>Hücre değeri (boşsa null, kırpılmış).</summary>
    public static string? Cell(string?[] cells, IReadOnlyDictionary<string, int> mapping, string field) =>
        mapping.TryGetValue(field, out var i) && i >= 0 && i < cells.Length && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i]!.Trim() : null;

    /// <summary>Önizleme: sayılar tüm satırlardan, liste hatalı/atlanan satırlar önce, sayfalı.</summary>
    public static ImportPreviewResponse Preview(IReadOnlyList<ImportRowResult> rows, ImportRowStatus? status, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var filtered = rows.Where(r => status is null || r.Status == status)
            .OrderBy(r => r.Status == ImportRowStatus.Ok).ThenBy(r => r.RowNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new ImportPreviewResponse(
            rows.Count,
            rows.Count(r => r.Status == ImportRowStatus.Ok),
            rows.Count(r => r.Status == ImportRowStatus.Error),
            rows.Count(r => r.Status == ImportRowStatus.Duplicate),
            filtered);
    }

    public static ImportCommitResponse CommitReport(IReadOnlyList<ImportRowResult> rows) => new(
        rows.Count(r => r.Status == ImportRowStatus.Ok),
        rows.Count(r => r.Status == ImportRowStatus.Duplicate),
        rows.Count(r => r.Status == ImportRowStatus.Error),
        rows.Where(r => r.Status != ImportRowStatus.Ok).ToList(),
        rows.Count(r => r.Status == ImportRowStatus.Ok && r.Messages.Count > 0));

    /// <summary>Oturumu tamamlandı olarak işaretler ve satır sonuçlarını rapor için saklar (çağıran SaveChanges yapar).</summary>
    public static async Task CompleteAsync(AppDbContext db, Guid jobId, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var tracked = await db.ImportJobs.FirstAsync(j => j.Id == jobId, ct);
        tracked.Status = ImportJobStatus.Completed;
        tracked.Result = JsonSerializer.Serialize(rows, Json);
    }

    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ReportAsync(
        AppDbContext db, Guid jobId, ImportKind kind, string detailTitle, CancellationToken ct)
    {
        var job = await db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.Kind == kind, ct);
        if (job?.Result is null)
        {
            return Problems.NotFound("Rapor bulunamadı.");
        }

        var rows = JsonSerializer.Deserialize<List<ImportRowResult>>(job.Result, Json)!;
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Rapor");
        LedgerEndpoints.Header(sheet, 1, "Satır", "Ad", detailTitle, "Durum", "Açıklama");
        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.RowNumber;
            sheet.Cell(r, 2).Value = row.Name;
            sheet.Cell(r, 3).Value = row.Detail;
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

    private static readonly string[] DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d.M.yy", "dd.MM.yy"];

    public static DateOnly? ParseDate(string text) =>
        DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>"Evet/Hayır" benzeri değer; anlaşılamazsa null.</summary>
    public static bool? ParseYesNo(string raw) => Simplify(raw) switch
    {
        "evet" or "e" or "var" or "1" or "x" or "true" or "izinli" or "onay" or "dahil" => true,
        "hayir" or "h" or "yok" or "0" or "false" or "izinsiz" or "haric" => false,
        _ => null,
    };

    /// <summary>"1.250,50", "1250,5", "1250.50", "-300", "1.250,50 TL" (Excel sayısı InvariantCulture metin olarak gelir).</summary>
    public static decimal? ParseMoney(string text)
    {
        var t = text.Replace("TL", "", StringComparison.OrdinalIgnoreCase).Replace("₺", "").Replace(" ", "").Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if (t.Contains(','))
        {
            // Türkçe biçim: nokta binlik, virgül ondalık.
            return decimal.TryParse(t.Replace(".", "").Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var tr) ? tr : null;
        }

        // Tek nokta ve ardından 3 hane binlik ayırıcı sayılır ("1.250"); diğer durumda ondalık.
        var parts = t.Split('.');
        if (parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 3 && t.TrimStart('-').Length > 4))
        {
            t = t.Replace(".", "");
        }

        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
