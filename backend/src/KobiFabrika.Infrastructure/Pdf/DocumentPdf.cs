using KobiFabrika.Domain.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KobiFabrika.Infrastructure.Pdf;

public sealed record PdfBusiness(string Name, byte[]? Logo, string? Phone, string? Email, string? Address);

/// <summary>Kalem: Quantity biçimlenmiş ("2 adet", "1,5 kg"); tutarlar KDV dahil.</summary>
public sealed record PdfLine(string Description, string Quantity, decimal UnitPrice, decimal Discount, decimal VatRate, decimal LineTotal);

public sealed record PdfVat(decimal Rate, decimal Base, decimal Vat);

/// <summary>SubTotal KDV dahil brüt; Discount satır + toplam indirim.</summary>
public sealed record PdfTotals(decimal SubTotal, decimal Discount, decimal VatTotal, decimal Total, IReadOnlyList<PdfVat> Vat);

public sealed record PdfPayment(string Label, decimal Amount);

/// <summary>WidthMm: kağıt genişliği (58 veya 80).</summary>
public sealed record ReceiptData(
    PdfBusiness Business, string No, DateTime DateLocal, string? Cashier, string? Customer, IReadOnlyList<PdfLine> Lines, PdfTotals Totals,
    IReadOnlyList<PdfPayment> Payments, bool Cancelled, string? Note, int WidthMm = 80);

public sealed record QuoteDocumentData(
    PdfBusiness Business, string No, DateOnly Date, DateOnly ValidUntil, StatementParty Customer, IReadOnlyList<PdfLine> Lines, PdfTotals Totals,
    string? Notes, DateTime GeneratedAtLocal);

/// <summary>
/// Satış fişi (58 veya 80 mm rulo) ve teklif (A4). Siyah-beyaz; fişte mali değer olmadığı açıkça yazar. Dar fişte uzun ürün adı
/// ve tutar satırları alt satıra kayar (kesilmez).
/// </summary>
internal sealed partial class QuestPdfRenderer
{
    /// <summary>Fişin alt yazısı (frontend fiş sayfası da aynı metni kullanır).</summary>
    public const string ReceiptFooter = "Bilgi fişidir, mali değeri yoktur.";

    private static string Percent(decimal rate) => $"%{rate:0.##}";

    public byte[] Receipt(ReceiptData d) => Document.Create(container => container.Page(page =>
    {
        var narrow = d.WidthMm < 80;
        page.ContinuousSize(d.WidthMm, Unit.Millimetre);
        page.Margin(narrow ? 3 : 4, Unit.Millimetre);
        page.DefaultTextStyle(t => t.FontSize(narrow ? 7 : 8).FontColor(Colors.Black));

        page.Content().Column(col =>
        {
            col.Spacing(2);
            if (d.Cancelled)
            {
                col.Item().Border(1).BorderColor(Colors.Black).Padding(2).AlignCenter().Text("İPTAL EDİLDİ").Bold();
            }

            if (d.Business.Logo is { Length: > 0 } logo)
            {
                col.Item().AlignCenter().Height(narrow ? 28 : 36).Image(logo).FitArea();
            }

            col.Item().AlignCenter().Text(d.Business.Name).FontSize(narrow ? 9 : 11).Bold();
            if (!string.IsNullOrWhiteSpace(d.Business.Address))
            {
                col.Item().AlignCenter().Text(d.Business.Address).FontColor(Gray);
            }

            if (!string.IsNullOrWhiteSpace(d.Business.Phone))
            {
                col.Item().AlignCenter().Text(d.Business.Phone).FontColor(Gray);
            }

            col.Item().PaddingVertical(3).LineHorizontal(0.5f).LineColor(Colors.Black);
            col.Item().Row(r =>
            {
                r.RelativeItem().Text(d.No).Bold();
                r.AutoItem().Text(d.DateLocal.ToString("dd.MM.yyyy HH:mm", Tr));
            });
            if (d.Cashier is not null)
            {
                col.Item().Text($"Kasiyer: {d.Cashier}");
            }

            if (d.Customer is not null)
            {
                col.Item().Text($"Müşteri: {d.Customer}");
            }

            col.Item().PaddingVertical(3).LineHorizontal(0.5f).LineColor(Colors.Black);
            foreach (var line in d.Lines)
            {
                col.Item().Text(line.Description).Bold();
                if (narrow)
                {
                    // 58 mm'de miktar × fiyat ve tutar yan yana sığmayabilir: tutar kendi satırında, sağa yaslı.
                    col.Item().Text($"{line.Quantity} × {Money.Format(line.UnitPrice)}  KDV {Percent(line.VatRate)}").FontColor(Gray);
                    col.Item().AlignRight().Text(Money.Format(line.LineTotal));
                }
                else
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"{line.Quantity} × {Money.Format(line.UnitPrice)}  KDV {Percent(line.VatRate)}").FontColor(Gray);
                        r.AutoItem().Text(Money.Format(line.LineTotal));
                    });
                }

                if (line.Discount > 0)
                {
                    col.Item().Text($"İndirim −{Money.Format(line.Discount)}").FontColor(Gray);
                }
            }

            col.Item().PaddingVertical(3).LineHorizontal(0.5f).LineColor(Colors.Black);
            if (d.Totals.Discount > 0)
            {
                TotalRow(col, "Ara toplam", Money.Format(d.Totals.SubTotal));
                TotalRow(col, "İndirim", "−" + Money.Format(d.Totals.Discount));
            }

            foreach (var vat in d.Totals.Vat.Where(v => v.Vat > 0))
            {
                TotalRow(col, $"KDV {Percent(vat.Rate)} (matrah {Money.Format(vat.Base)})", Money.Format(vat.Vat));
            }

            col.Item().Row(r =>
            {
                r.RelativeItem().Text("TOPLAM").FontSize(narrow ? 9 : 11).Bold();
                r.AutoItem().Text(Money.Format(d.Totals.Total)).FontSize(narrow ? 9 : 11).Bold();
            });

            col.Item().PaddingVertical(3).LineHorizontal(0.5f).LineColor(Line);
            foreach (var payment in d.Payments)
            {
                TotalRow(col, payment.Label, Money.Format(payment.Amount));
            }

            if (!string.IsNullOrWhiteSpace(d.Note))
            {
                col.Item().PaddingTop(3).Text(d.Note).FontColor(Gray);
            }

            col.Item().PaddingTop(6).AlignCenter().Text(ReceiptFooter).Bold();
            col.Item().AlignCenter().Text("KobiFabrika").FontSize(7).FontColor(Gray);
        });
    })).GeneratePdf();

    private static void TotalRow(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(label);
            r.AutoItem().Text(value);
        });

    public byte[] Quote(QuoteDocumentData d) => Document.Create(container => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Black));

        page.Header().Column(col =>
        {
            col.Item().Row(row =>
            {
                if (d.Business.Logo is { Length: > 0 } logo)
                {
                    row.ConstantItem(64).Height(48).Image(logo).FitArea();
                    row.ConstantItem(12);
                }

                row.RelativeItem().Column(info =>
                {
                    info.Item().Text(d.Business.Name).FontSize(14).Bold();
                    var line = string.Join(" · ", new[] { d.Business.Phone, d.Business.Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (line.Length > 0)
                    {
                        info.Item().Text(line).FontColor(Gray);
                    }

                    if (!string.IsNullOrWhiteSpace(d.Business.Address))
                    {
                        info.Item().Text(d.Business.Address).FontColor(Gray);
                    }
                });

                row.ConstantItem(170).AlignRight().Column(meta =>
                {
                    meta.Item().AlignRight().Text("Teklif").FontSize(12).Bold();
                    meta.Item().AlignRight().Text(d.No);
                    meta.Item().AlignRight().Text($"Tarih: {d.Date.ToString("dd.MM.yyyy", Tr)}").FontColor(Gray);
                    meta.Item().AlignRight().Text($"Geçerlilik: {d.ValidUntil.ToString("dd.MM.yyyy", Tr)}").FontColor(Gray);
                });
            });

            col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Black);
            col.Item().Column(c =>
            {
                c.Item().Text("Sayın").FontColor(Gray);
                c.Item().Text(d.Customer.Name).FontSize(11).Bold();
                var contactLine = string.Join(" · ", new[] { d.Customer.Phone, d.Customer.Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (contactLine.Length > 0)
                {
                    c.Item().Text(contactLine).FontColor(Gray);
                }

                if (!string.IsNullOrWhiteSpace(d.Customer.Address))
                {
                    c.Item().Text(d.Customer.Address).FontColor(Gray);
                }
            });
            col.Item().PaddingBottom(8);
        });

        page.Content().Column(content =>
        {
            content.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.ConstantColumn(62);
                    c.ConstantColumn(74);
                    c.ConstantColumn(62);
                    c.ConstantColumn(38);
                    c.ConstantColumn(80);
                });

                table.Header(h =>
                {
                    static IContainer Head(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Black).PaddingVertical(4);
                    h.Cell().Element(Head).Text("Ürün / Hizmet").Bold();
                    h.Cell().Element(Head).AlignRight().Text("Miktar").Bold();
                    h.Cell().Element(Head).AlignRight().Text("Birim fiyat").Bold();
                    h.Cell().Element(Head).AlignRight().Text("İndirim").Bold();
                    h.Cell().Element(Head).AlignRight().Text("KDV").Bold();
                    h.Cell().Element(Head).AlignRight().Text("Tutar").Bold();
                });

                static IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4);
                foreach (var line in d.Lines)
                {
                    table.Cell().Element(Cell).Text(line.Description);
                    table.Cell().Element(Cell).AlignRight().Text(line.Quantity);
                    table.Cell().Element(Cell).AlignRight().Text(Money.Format(line.UnitPrice));
                    table.Cell().Element(Cell).AlignRight().Text(line.Discount > 0 ? Money.Format(line.Discount) : "");
                    table.Cell().Element(Cell).AlignRight().Text(Percent(line.VatRate));
                    table.Cell().Element(Cell).AlignRight().Text(Money.Format(line.LineTotal));
                }
            });

            content.Item().PaddingTop(10).AlignRight().Width(240).Column(totals =>
            {
                totals.Spacing(2);
                TotalRow(totals, "Ara toplam (KDV dahil)", Money.Format(d.Totals.SubTotal));
                if (d.Totals.Discount > 0)
                {
                    TotalRow(totals, "İndirim", "−" + Money.Format(d.Totals.Discount));
                }

                foreach (var vat in d.Totals.Vat.Where(v => v.Vat > 0))
                {
                    TotalRow(totals, $"KDV {Percent(vat.Rate)} (matrah {Money.Format(vat.Base)})", Money.Format(vat.Vat));
                }

                totals.Item().BorderTop(1).BorderColor(Colors.Black).PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text("Genel toplam").FontSize(11).Bold();
                    r.AutoItem().Text(Money.Format(d.Totals.Total)).FontSize(11).Bold();
                });
            });

            if (!string.IsNullOrWhiteSpace(d.Notes))
            {
                content.Item().PaddingTop(16).Text("Notlar").Bold();
                content.Item().Text(d.Notes);
            }

            content.Item().PaddingTop(16).Text($"Bu teklif {d.ValidUntil.ToString("dd.MM.yyyy", Tr)} tarihine kadar geçerlidir.").Bold();
        });

        page.Footer().Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span($"{d.GeneratedAtLocal.ToString("dd.MM.yyyy HH:mm", Tr)} tarihinde oluşturuldu · ").FontColor(Gray).FontSize(8);
                t.Span("KobiFabrika ile hazırlandı").FontColor(Gray).FontSize(8);
            });
            row.ConstantItem(60).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontSize(8).FontColor(Gray);
                t.Span(" / ").FontSize(8).FontColor(Gray);
                t.TotalPages().FontSize(8).FontColor(Gray);
            });
        });
    })).GeneratePdf();
}
