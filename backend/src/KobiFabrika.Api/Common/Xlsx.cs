using ClosedXML.Excel;

namespace KobiFabrika.Api.Common;

/// <summary>
/// Excel çıktılarının ortak biçimi (ClosedXML, MIT): Türkçe kalın başlık, tarih "dd.MM.yyyy", saat İstanbul,
/// para "1.234,50 TL" (hücre sayı kalır, toplanabilir), sütun genişliği içeriğe göre.
/// </summary>
public static class Xlsx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string DateFormat = "dd.MM.yyyy";
    public const string DateTimeFormat = "dd.MM.yyyy HH:mm";
    public const string MoneyFormat = "#,##0.00 \"TL\"";
    public const string QuantityFormat = "#,##0.###";

    public static void Header(IXLWorksheet sheet, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            cell.Value = titles[i];
            cell.Style.Font.Bold = true;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
    }

    /// <summary>Satırı yazar; DateOnly tarihe, decimal sayıya çevrilir.</summary>
    public static void Row(IXLWorksheet sheet, int row, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            sheet.Cell(row, i + 1).Value = values[i] switch
            {
                DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                var v => XLCellValue.FromObject(v),
            };
        }
    }

    /// <summary>Başlık satırından sonraki satırlarda sütun biçimleri (1 tabanlı sütun numaraları).</summary>
    public static void Format(IXLWorksheet sheet, int firstRow, int lastRow, IEnumerable<int>? money = null, IEnumerable<int>? dates = null,
        IEnumerable<int>? dateTimes = null, IEnumerable<int>? quantities = null)
    {
        lastRow = Math.Max(lastRow, firstRow);
        foreach (var c in money ?? [])
        {
            sheet.Range(firstRow, c, lastRow, c).Style.NumberFormat.Format = MoneyFormat;
        }

        foreach (var c in dates ?? [])
        {
            sheet.Range(firstRow, c, lastRow, c).Style.DateFormat.Format = DateFormat;
        }

        foreach (var c in dateTimes ?? [])
        {
            sheet.Range(firstRow, c, lastRow, c).Style.DateFormat.Format = DateTimeFormat;
        }

        foreach (var c in quantities ?? [])
        {
            sheet.Range(firstRow, c, lastRow, c).Style.NumberFormat.Format = QuantityFormat;
        }

        sheet.Columns().AdjustToContents();
    }

    public static byte[] Save(XLWorkbook book)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
