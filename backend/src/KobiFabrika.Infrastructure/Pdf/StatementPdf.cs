using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KobiFabrika.Infrastructure.Pdf;

public sealed record StatementRow(DateOnly Date, string Description, decimal Debit, decimal Credit, decimal Balance);

public sealed record StatementParty(string Name, string? Phone, string? Email, string? Address);

/// <summary>Ekstre PDF'inin ve herkese açık ekstre sayfasının ortak verisi.</summary>
public sealed record StatementData(
    string BusinessName,
    byte[]? Logo,
    string? BusinessPhone,
    string? BusinessEmail,
    string? BusinessAddress,
    StatementParty Contact,
    DateOnly? From,
    DateOnly To,
    decimal Carried,
    IReadOnlyList<StatementRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Closing,
    DateTime GeneratedAtLocal);

/// <summary>PDF üretimi (QuestPDF). Arayüz, testte ve ileride başka uygulamalarla değiştirilebilsin diye.</summary>
public interface IPdfRenderer
{
    byte[] Statement(StatementData data);
}

/// <summary>
/// Cari hesap ekstresi: üstte işletme logosu ve bilgileri, müşteri, tarih aralığı, devreden bakiye, hareket tablosu
/// (Tarih, Açıklama, Borç, Alacak, Bakiye), son bakiye "Alacak"/"Borç" diliyle. Sadece siyah/gri (docs/04-tasarim.md).
/// QuestPDF'in gömülü Lato fontu Türkçe karakterleri destekler.
/// </summary>
internal sealed class QuestPdfRenderer : IPdfRenderer
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private const string Gray = "#525252";
    private const string Line = "#d4d4d4";

    public static string Money(decimal value) => value.ToString("N2", Tr) + " TL";

    /// <summary>Bakiye etiketi: pozitif = Alacak (müşteri bize borçlu), negatif = Borç (biz borçluyuz).</summary>
    public static string BalanceText(decimal balance) => balance switch
    {
        > 0 => $"Alacak {Money(balance)}",
        < 0 => $"Borç {Money(-balance)}",
        _ => "Bakiye yok",
    };

    public byte[] Statement(StatementData d) => Document.Create(container => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Black));

        page.Header().Column(col =>
        {
            col.Item().Row(row =>
            {
                if (d.Logo is { Length: > 0 } logo)
                {
                    row.ConstantItem(64).Height(48).Image(logo).FitArea();
                    row.ConstantItem(12);
                }

                row.RelativeItem().Column(info =>
                {
                    info.Item().Text(d.BusinessName).FontSize(14).Bold();
                    var line = string.Join(" · ", new[] { d.BusinessPhone, d.BusinessEmail }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (line.Length > 0)
                    {
                        info.Item().Text(line).FontColor(Gray);
                    }

                    if (!string.IsNullOrWhiteSpace(d.BusinessAddress))
                    {
                        info.Item().Text(d.BusinessAddress).FontColor(Gray);
                    }
                });

                row.ConstantItem(170).AlignRight().Column(meta =>
                {
                    meta.Item().AlignRight().Text("Cari Hesap Ekstresi").FontSize(12).Bold();
                    meta.Item().AlignRight().Text(Range(d)).FontColor(Gray);
                });
            });

            col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Black);

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Sayın").FontColor(Gray);
                    c.Item().Text(d.Contact.Name).FontSize(11).Bold();
                    var contactLine = string.Join(" · ", new[] { d.Contact.Phone, d.Contact.Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (contactLine.Length > 0)
                    {
                        c.Item().Text(contactLine).FontColor(Gray);
                    }

                    if (!string.IsNullOrWhiteSpace(d.Contact.Address))
                    {
                        c.Item().Text(d.Contact.Address).FontColor(Gray);
                    }
                });

                row.ConstantItem(170).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text("Son bakiye").FontColor(Gray);
                    c.Item().AlignRight().Text(BalanceText(d.Closing)).FontSize(12).Bold();
                });
            });

            col.Item().PaddingBottom(8);
        });

        page.Content().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(62);
                c.RelativeColumn();
                c.ConstantColumn(78);
                c.ConstantColumn(78);
                c.ConstantColumn(92);
            });

            table.Header(h =>
            {
                static IContainer Head(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Black).PaddingVertical(4);
                h.Cell().Element(Head).Text("Tarih").Bold();
                h.Cell().Element(Head).Text("Açıklama").Bold();
                h.Cell().Element(Head).AlignRight().Text("Borç").Bold();
                h.Cell().Element(Head).AlignRight().Text("Alacak").Bold();
                h.Cell().Element(Head).AlignRight().Text("Bakiye").Bold();
            });

            static IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4);

            if (d.From is not null)
            {
                table.Cell().Element(Cell).Text(d.From.Value.ToString("dd.MM.yyyy", Tr));
                table.Cell().Element(Cell).Text("Devreden bakiye").Italic();
                table.Cell().Element(Cell);
                table.Cell().Element(Cell);
                table.Cell().Element(Cell).AlignRight().Text(Signed(d.Carried));
            }

            foreach (var r in d.Rows)
            {
                table.Cell().Element(Cell).Text(r.Date.ToString("dd.MM.yyyy", Tr));
                table.Cell().Element(Cell).Text(r.Description);
                table.Cell().Element(Cell).AlignRight().Text(r.Debit > 0 ? Money(r.Debit) : "");
                table.Cell().Element(Cell).AlignRight().Text(r.Credit > 0 ? Money(r.Credit) : "");
                table.Cell().Element(Cell).AlignRight().Text(Signed(r.Balance));
            }

            if (d.Rows.Count == 0)
            {
                table.Cell().ColumnSpan(5).PaddingVertical(12).Text("Bu tarih aralığında hareket yok.").FontColor(Gray);
            }

            static IContainer Total(IContainer c) => c.BorderTop(1).BorderColor(Colors.Black).PaddingVertical(4);
            table.Cell().Element(Total);
            table.Cell().Element(Total).Text("Toplam").Bold();
            table.Cell().Element(Total).AlignRight().Text(Money(d.TotalDebit)).Bold();
            table.Cell().Element(Total).AlignRight().Text(Money(d.TotalCredit)).Bold();
            table.Cell().Element(Total).AlignRight().Text(Signed(d.Closing)).Bold();
        });

        page.Footer().Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span($"{d.GeneratedAtLocal.ToString("dd.MM.yyyy HH:mm", Tr)} tarihinde oluşturuldu · ").FontColor(Gray).FontSize(8);
                t.Span("KobiFabrika ile gönderildi").FontColor(Gray).FontSize(8);
            });
            row.ConstantItem(60).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontSize(8).FontColor(Gray);
                t.Span(" / ").FontSize(8).FontColor(Gray);
                t.TotalPages().FontSize(8).FontColor(Gray);
            });
        });
    })).GeneratePdf();

    /// <summary>Tablodaki bakiye: Alacak "(A)", Borç "(B)" ekiyle; renge dayanmaz.</summary>
    private static string Signed(decimal balance) => balance switch
    {
        > 0 => Money(balance) + " (A)",
        < 0 => Money(-balance) + " (B)",
        _ => Money(0),
    };

    private static string Range(StatementData d) => d.From is { } from
        ? $"{from.ToString("dd.MM.yyyy", Tr)} – {d.To.ToString("dd.MM.yyyy", Tr)}"
        : $"{d.To.ToString("dd.MM.yyyy", Tr)} tarihine kadar";
}
