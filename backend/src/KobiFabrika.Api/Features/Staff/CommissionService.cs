using System.Globalization;
using KobiFabrika.Api.Features.Finance;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Sales;
using KobiFabrika.Domain.Staff;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Staff;

/// <summary>Prim kuralının ekranda ve prim kaydında görünen metni: "Ayşe Kaya · Saç kesimi · %10".</summary>
public static class CommissionText
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Who(string? staffName) => staffName ?? "Herkes";

    public static string Target(CommissionTarget target, string? categoryName, string? productName) => target switch
    {
        CommissionTarget.Service => "Hizmetler",
        CommissionTarget.Product => "Ürünler",
        CommissionTarget.Category => $"Kategori: {categoryName ?? "(silinmiş)"}",
        CommissionTarget.Item => productName ?? "(silinmiş ürün)",
        _ => "Bütün satışlar",
    };

    public static string Rate(CommissionType type, decimal value) =>
        type == CommissionType.Percent ? "%" + value.ToString("0.##", Tr) : ShareLinks.Money(value) + "/adet";

    public static string Describe(CommissionRule rule, string? staffName, string? categoryName, string? productName) =>
        $"{Who(staffName)} · {Target(rule.Target, categoryName, productName)} · {Rate(rule.Type, rule.Value)}";
}

/// <summary>
/// Prim tahakkuku ve iptali; sadece <see cref="Sales.SaleService"/> içinden, satışın transaction'ında çağrılır.
/// Satışta personeli olan ve tutarı 0'dan büyük her satıra en özel kural uygulanır (<see cref="CommissionRuleResolver"/>);
/// paket satırı paketin hizmetiyle eşleşir, prim satış anında yazılır (seans kullanımında prim yok). İptalde her tahakkuka
/// karşılık iptal günü ve ayıyla eksi kayıt yazılır; kapanmış ayın tutarı değişmez. Satır başına tek tahakkuk ve tek iptal
/// (benzersiz indeks).
/// </summary>
public sealed class CommissionService(AppDbContext db, TimeProvider clock, KobiFabrika.Infrastructure.Billing.IPlanAccess plans)
{
    /// <summary>Satışın satırları kaydedildikten (Id ve paket bağlantısı belli) sonra çağrılır; SaveChanges çağıran tarafta.</summary>
    public async Task AccrueAsync(Sale sale, CancellationToken ct)
    {
        var lines = sale.Lines.Where(l => l.StaffMemberId is not null && l.LineTotal - l.VatAmount > 0).ToList();
        // Prim Usta'da (karar 2026-10-17): paket kapsamıyorsa prim yazılmaz; geçmiş primler kalır, iptalde eksi kaydı yine yazılır.
        if (lines.Count == 0 || !(await plans.GetAsync(ct)).Has(Domain.Billing.PlanFeatures.Commissions))
        {
            return;
        }

        var rules = await db.CommissionRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
        if (rules.Count == 0)
        {
            return;
        }

        var packageIds = lines.Select(l => l.ServicePackageId).OfType<Guid>().Distinct().ToList();
        var packageServices = packageIds.Count == 0 ? new Dictionary<Guid, Guid>() : await db.ServicePackages.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(p => packageIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ProductId, ct);
        Guid? ProductOf(SaleLine l) => l.ProductId ?? (l.ServicePackageId is { } p ? packageServices.GetValueOrDefault(p) : null);

        var productIds = lines.Select(ProductOf).OfType<Guid>().Concat(rules.Select(r => r.ProductId).OfType<Guid>()).Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Type, p.CategoryId, p.Name })
            .ToDictionaryAsync(p => p.Id, ct);

        var day = BusinessTime.LocalDay(sale.Date);
        var texts = new Dictionary<Guid, string>();
        foreach (var line in lines)
        {
            var productId = ProductOf(line);
            var product = productId is { } id ? products.GetValueOrDefault(id) : null;
            var net = line.LineTotal - line.VatAmount;
            var candidate = new CommissionLine(line.StaffMemberId, productId, product?.Type, product?.CategoryId, net, line.Quantity);
            if (CommissionRuleResolver.Resolve(rules, candidate) is not { } rule)
            {
                continue;
            }

            var amount = CommissionCalculator.Amount(rule.Type, rule.Value, net, line.Quantity);
            if (amount <= 0)
            {
                continue;
            }

            if (!texts.TryGetValue(rule.Id, out var text))
            {
                text = await DescribeAsync(rule, rule.ProductId is { } rp ? products.GetValueOrDefault(rp)?.Name : null, ct);
                texts[rule.Id] = text;
            }

            db.Commissions.Add(new Commission
            {
                StaffMemberId = line.StaffMemberId!.Value, SaleId = sale.Id, SaleLineId = line.Id, CommissionRuleId = rule.Id, RuleText = text,
                Base = net, Type = rule.Type, Rate = rule.Value, Quantity = line.Quantity, Amount = amount, Kind = CommissionKind.Accrual,
                OccurredOn = day, Period = Commission.PeriodOf(day),
            });
        }
    }

    /// <summary>Satış iptalinde (satış satırı koşullu UPDATE ile kilitliyken) her tahakkuka eksi kayıt; SaveChanges çağıran tarafta.</summary>
    public async Task ReverseAsync(Guid saleId, CancellationToken ct)
    {
        var accruals = await db.Commissions.AsNoTracking()
            .Where(c => c.SaleId == saleId && c.Kind == CommissionKind.Accrual)
            .Where(c => !db.Commissions.Any(r => r.ReversalOfId == c.Id))
            .ToListAsync(ct);
        var today = BusinessTime.Today(clock);
        foreach (var a in accruals)
        {
            db.Commissions.Add(new Commission
            {
                StaffMemberId = a.StaffMemberId, SaleId = a.SaleId, SaleLineId = a.SaleLineId, CommissionRuleId = a.CommissionRuleId,
                RuleText = a.RuleText, Base = a.Base, Type = a.Type, Rate = a.Rate, Quantity = a.Quantity,
                Amount = CommissionCalculator.ReversalAmount(a.Amount), Kind = CommissionKind.Reversal, ReversalOfId = a.Id,
                OccurredOn = today, Period = Commission.PeriodOf(today),
            });
        }
    }

    private async Task<string> DescribeAsync(CommissionRule rule, string? productName, CancellationToken ct)
    {
        var staffName = rule.StaffMemberId is { } s
            ? await db.StaffMembers.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(x => x.Id == s).Select(x => x.FullName).FirstOrDefaultAsync(ct)
            : null;
        var categoryName = rule.CategoryId is { } c
            ? await db.ProductCategories.IgnoreQueryFilters([QueryFilters.SoftDelete]).Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;
        return CommissionText.Describe(rule, staffName, categoryName, productName);
    }
}
