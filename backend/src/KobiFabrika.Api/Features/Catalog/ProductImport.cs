using KobiFabrika.Api.Features.Imports;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>İçe aktarmaya hazır, doğrulanmış ürün satırı.</summary>
public sealed record ProductCandidate(
    int RowNumber,
    string Name,
    ProductType Type,
    string? Category,
    ProductUnit Unit,
    decimal VatRate,
    bool PricesIncludeVat,
    decimal SalePrice,
    decimal? PurchasePrice,
    string? Barcode,
    int? DurationMinutes,
    decimal? Stock,
    decimal? CriticalStockLevel);

/// <summary>
/// Excel'den ürün/hizmet içe aktarma kuralları. Ad ve satış fiyatı zorunlu; tür boşsa Ürün, birim boşsa Adet, KDV boşsa %20 (uyarıyla),
/// "Fiyat KDV dahil" boşsa Evet. Barkod veritabanında veya dosyada daha önce varsa satır atlanır; barkodsuz satırda aynı ad ve tür
/// varsa atlanır. Olmayan kategori oluşturulur. Stok sadece Stok modülü açıksa ve ürünlerde aktarılır ("Açılış stoğu" hareketi).
/// </summary>
public static class ProductImport
{
    public static readonly IReadOnlyList<ImportFieldInfo> Fields =
    [
        new("name", "Ad"),
        new("type", "Tür"),
        new("category", "Kategori"),
        new("unit", "Birim"),
        new("vatRate", "KDV oranı"),
        new("pricesIncludeVat", "Fiyat KDV dahil"),
        new("salePrice", "Satış fiyatı"),
        new("purchasePrice", "Alış fiyatı"),
        new("barcode", "Barkod"),
        new("durationMinutes", "Süre (dk)"),
        new("stock", "Stok"),
        new("criticalStockLevel", "Kritik stok"),
    ];

    public static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>
    {
        ["name"] = ["urun adi", "urun", "hizmet adi", "hizmet", "adi", "isim", "urun/hizmet adi", "stok adi", "malzeme adi"],
        ["type"] = ["tur", "tip", "urun/hizmet", "cins"],
        ["category"] = ["kategori", "grup", "urun grubu", "kategori adi"],
        ["unit"] = ["birim", "olcu birimi", "birimi"],
        ["vatRate"] = ["kdv", "kdv %", "kdv orani %", "kdv yuzdesi", "vergi"],
        ["pricesIncludeVat"] = ["kdv dahil", "fiyat kdv dahil mi", "kdv dahil mi"],
        ["salePrice"] = ["satis fiyati", "fiyat", "fiyati", "satis", "birim fiyat", "etiket fiyati"],
        ["purchasePrice"] = ["alis fiyati", "alis", "maliyet", "alis fiyat"],
        ["barcode"] = ["barkod", "barkod no", "barkodu", "ean", "barcode"],
        ["durationMinutes"] = ["sure", "sure dk", "dakika", "hizmet suresi"],
        ["stock"] = ["stok", "stok miktari", "mevcut", "mevcut stok", "miktar"],
        ["criticalStockLevel"] = ["kritik stok", "kritik seviye", "minimum stok", "min stok", "kritik stok seviyesi"],
    };

    private static readonly Dictionary<string, ProductUnit> UnitNames = new()
    {
        ["adet"] = ProductUnit.Piece, ["ad"] = ProductUnit.Piece, ["tane"] = ProductUnit.Piece,
        ["kg"] = ProductUnit.Kg, ["kilo"] = ProductUnit.Kg, ["kilogram"] = ProductUnit.Kg,
        ["lt"] = ProductUnit.Liter, ["l"] = ProductUnit.Liter, ["litre"] = ProductUnit.Liter,
        ["m"] = ProductUnit.Meter, ["mt"] = ProductUnit.Meter, ["metre"] = ProductUnit.Meter,
        ["paket"] = ProductUnit.Package, ["pk"] = ProductUnit.Package, ["koli"] = ProductUnit.Package,
        ["saat"] = ProductUnit.Hour, ["sa"] = ProductUnit.Hour,
        ["seans"] = ProductUnit.Session,
    };

    public static Dictionary<string, int> SuggestMapping(IReadOnlyList<string> headers) => ImportJobs.SuggestMapping(headers, Fields, Aliases);

    public static byte[] Template() => ImportJobs.Template("Ürünler", Fields, [9],
    [
        ["Alan", "Nasıl doldurulur", "Örnek"],
        ["Ad", "Zorunlu.", "Pamuklu tişört"],
        ["Tür", "Ürün veya Hizmet. Boş = Ürün.", "Ürün"],
        ["Kategori", "İsteğe bağlı; olmayan kategori oluşturulur.", "Giyim"],
        ["Birim", "Adet, Kg, Lt, M, Paket, Saat, Seans. Boş = Adet.", "Adet"],
        ["KDV oranı", "0, 1, 10 veya 20. Boş = 20.", "20"],
        ["Fiyat KDV dahil", "Evet veya Hayır. Boş = Evet (fiyatlar KDV dahil).", "Evet"],
        ["Satış fiyatı", "Zorunlu.", "249,90"],
        ["Alış fiyatı", "İsteğe bağlı.", "120"],
        ["Barkod", "İsteğe bağlı. Aynı barkod zaten kayıtlıysa satır atlanır.", "8690000000011"],
        ["Süre (dk)", "Sadece hizmetlerde (randevu süresi).", "45"],
        ["Stok", "Sadece ürünlerde ve Stok modülü açıkken; açılış stoğu olarak girilir.", "25"],
        ["Kritik stok", "Stok bu sayıya inince bildirim gelir.", "5"],
        ["", "En fazla 5000 satır. Bu sayfa içe aktarılmaz.", ""],
    ]);

    /// <summary>Satırı doğrular. Hata varsa <c>Candidate</c> null.</summary>
    public static (ProductCandidate? Candidate, List<string> Errors, List<string> Warnings) Parse(
        int rowNumber, string?[] cells, IReadOnlyDictionary<string, int> mapping, IReadOnlySet<decimal> vatRates, bool stockEnabled)
    {
        string? Get(string field) => ImportJobs.Cell(cells, mapping, field);
        var errors = new List<string>();
        var warnings = new List<string>();

        var name = Get("name");
        if (name is null)
        {
            errors.Add("Ad yok.");
        }
        else if (name.Length > Product.MaxNameLength)
        {
            errors.Add("Ad en fazla 200 karakter olabilir.");
        }

        var type = ProductType.Product;
        if (Get("type") is { } rawType)
        {
            switch (ImportJobs.Simplify(rawType))
            {
                case "urun" or "mal" or "product":
                    break;
                case "hizmet" or "servis" or "service":
                    type = ProductType.Service;
                    break;
                default:
                    errors.Add($"Tür için Ürün veya Hizmet yazın: {rawType}");
                    break;
            }
        }

        var category = Get("category");
        if (category?.Length > 100)
        {
            errors.Add("Kategori adı en fazla 100 karakter olabilir.");
        }

        var unit = ProductUnit.Piece;
        if (Get("unit") is { } rawUnit)
        {
            if (UnitNames.TryGetValue(ImportJobs.Simplify(rawUnit).TrimEnd('.'), out var parsedUnit))
            {
                unit = parsedUnit;
            }
            else
            {
                errors.Add($"Birim anlaşılamadı: {rawUnit} (Adet, Kg, Lt, M, Paket, Saat, Seans)");
            }
        }

        var vatRate = 20m;
        if (Get("vatRate") is { } rawVat)
        {
            if (ParseVatRate(rawVat) is { } rate && vatRates.Contains(rate))
            {
                vatRate = rate;
            }
            else
            {
                errors.Add($"KDV oranı geçersiz: {rawVat} (0, 1, 10 veya 20)");
            }
        }
        else
        {
            warnings.Add("KDV oranı boş; %20 alındı.");
        }

        var includesVat = true;
        if (Get("pricesIncludeVat") is { } rawIncludes)
        {
            if (ImportJobs.ParseYesNo(rawIncludes) is { } value)
            {
                includesVat = value;
            }
            else
            {
                errors.Add($"Fiyat KDV dahil için Evet veya Hayır yazın: {rawIncludes}");
            }
        }

        decimal? Price(string field, string label, bool required)
        {
            if (Get(field) is not { } raw)
            {
                if (required)
                {
                    errors.Add($"{label} yok.");
                }

                return null;
            }

            if (ImportJobs.ParseMoney(raw) is { } price && ProductRequestValidator.ValidPrice(price))
            {
                return price;
            }

            errors.Add($"{label} geçersiz: {raw}");
            return null;
        }

        var salePrice = Price("salePrice", "Satış fiyatı", required: true);
        var purchasePrice = Price("purchasePrice", "Alış fiyatı", required: false);

        var barcode = ProductEndpoints.NormalizeBarcode(Get("barcode"));
        if (barcode is not null && (barcode.Length > Product.MaxBarcodeLength || barcode.Any(char.IsWhiteSpace)))
        {
            errors.Add("Barkod en fazla 64 karakter olabilir ve boşluk içeremez.");
        }

        int? duration = null;
        if (Get("durationMinutes") is { } rawDuration && type == ProductType.Service)
        {
            if (int.TryParse(rawDuration, out var minutes) && minutes is >= 1 and <= 1440)
            {
                duration = minutes;
            }
            else
            {
                errors.Add($"Süre 1 ile 1440 dakika arasında olmalı: {rawDuration}");
            }
        }

        decimal? Quantity(string field, string label)
        {
            if (Get(field) is not { } raw || type == ProductType.Service)
            {
                return null;
            }

            if (!stockEnabled)
            {
                warnings.Add($"{label} aktarılmadı (Stok modülü kapalı).");
                return null;
            }

            var value = ImportJobs.ParseMoney(raw);
            if (Quantities.Validate(value, unit, allowZero: true) is { } error)
            {
                errors.Add($"{label}: {(value is null ? $"anlaşılamadı ({raw})" : error)}");
                return null;
            }

            return value;
        }

        var stock = Quantity("stock", "Stok");
        var critical = Quantity("criticalStockLevel", "Kritik stok");

        if (errors.Count > 0)
        {
            return (null, errors, warnings);
        }

        return (new ProductCandidate(rowNumber, name!, type, category, unit, vatRate, includesVat, salePrice!.Value, purchasePrice, barcode, duration,
            stock, critical), errors, warnings);
    }

    /// <summary>"20", "%20", "20%", "0,20", "0.2" → 20.</summary>
    public static decimal? ParseVatRate(string raw)
    {
        if (ImportJobs.ParseMoney(raw.Replace("%", "")) is not { } value)
        {
            return null;
        }

        return value is > 0 and < 1 ? value * 100 : value;
    }
}
