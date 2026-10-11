using System.Data;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Catalog;
using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Api.Features.Catalog;

/// <summary>Stok hareketinin sonucu: hareket satırı ve öncesi/sonrası miktar.</summary>
public sealed record StockChange(StockMovement Movement, decimal Before, decimal After);

/// <summary>
/// Stok miktarının tek yazıldığı yer (LedgerService gibi). Hareket satırı ve <c>Products.StockQuantity</c> aynı transaction'da
/// yazılır; miktar SQL'de atomik <c>UPDATE … OUTPUT</c> ile değişir, aynı ürüne eşzamanlı satışlarda kayıp olmaz.
/// Stok kritik seviyeye inince Sahip ve Yöneticilere bir kez bildirim gider (<c>LowStockNotifiedAt</c> koşullu güncellenir);
/// stok kritiğin üstüne çıkınca işaret temizlenir, sonraki düşüşte yeniden bildirilir. Eksi stoğa izin verilir.
/// </summary>
public sealed class StockService(AppDbContext db, ITenantContext tenant, INotificationService notifications, TimeProvider clock)
{
    private sealed class StockRow
    {
        public decimal Before { get; init; }
        public decimal After { get; init; }
        public decimal? Critical { get; init; }
        public DateTime? NotifiedAt { get; init; }
        public required string Name { get; init; }
        public required string Unit { get; init; }
    }

    /// <summary>
    /// Stok hareketi ekler. <paramref name="delta"/> işaretli değişim; sayımda <paramref name="counted"/> verilir ve fark hesaplanır.
    /// Sayım mevcut stokla aynıysa hareket yazılmaz (null). Transaction içinde çağrılmalı.
    /// </summary>
    public async Task<StockChange?> ApplyAsync(
        Guid productId, StockMovementType type, decimal? delta, decimal? counted, DateOnly date, decimal? unitCost, Guid? saleId, string? note,
        CancellationToken ct)
    {
        var businessId = tenant.BusinessId ?? throw new TenantViolationException("Kiracı bağlamı olmadan stok yazılamaz.");
        var rows = counted is { } target
            ? await db.Database.SqlQuery<StockRow>($"""
                UPDATE [Products] SET [StockQuantity] = {QuantityParameter(target)}
                OUTPUT deleted.[StockQuantity] AS [Before], inserted.[StockQuantity] AS [After], inserted.[CriticalStockLevel] AS [Critical],
                       inserted.[LowStockNotifiedAt] AS [NotifiedAt], inserted.[Name] AS [Name], inserted.[Unit] AS [Unit]
                WHERE [Id] = {productId} AND [BusinessId] = {businessId} AND [TrackStock] = 1
                """).ToListAsync(ct)
            : await db.Database.SqlQuery<StockRow>($"""
                UPDATE [Products] SET [StockQuantity] = [StockQuantity] + {QuantityParameter(delta!.Value)}
                OUTPUT deleted.[StockQuantity] AS [Before], inserted.[StockQuantity] AS [After], inserted.[CriticalStockLevel] AS [Critical],
                       inserted.[LowStockNotifiedAt] AS [NotifiedAt], inserted.[Name] AS [Name], inserted.[Unit] AS [Unit]
                WHERE [Id] = {productId} AND [BusinessId] = {businessId} AND [TrackStock] = 1
                """).ToListAsync(ct);

        if (rows is not [var row])
        {
            throw new InvalidOperationException("Stoğu izlenen ürün bulunamadı.");
        }

        var change = row.After - row.Before;
        if (change == 0)
        {
            return null;
        }

        var movement = new StockMovement
        {
            ProductId = productId,
            Type = type,
            Quantity = change,
            QuantityAfter = row.After,
            UnitCost = unitCost,
            Date = date,
            SaleId = saleId,
            Note = note,
        };
        db.StockMovements.Add(movement);
        await NotifyLowStockAsync(productId, businessId, row, ct);
        await db.SaveChangesAsync(ct);
        return new StockChange(movement, row.Before, row.After);
    }

    /// <summary>Miktar parametresi decimal(18,3); EF'in varsayılan decimal(18,2) parametresi 2,375'i 2,38'e yuvarlar.</summary>
    private static SqlParameter QuantityParameter(decimal value) =>
        new() { SqlDbType = SqlDbType.Decimal, Precision = 18, Scale = 3, Value = value };

    private async Task NotifyLowStockAsync(Guid productId, Guid businessId, StockRow row, CancellationToken ct)
    {
        if (row.Critical is not { } critical)
        {
            return;
        }

        if (row.After > critical)
        {
            if (row.NotifiedAt is not null)
            {
                await db.Products.Where(p => p.Id == productId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.LowStockNotifiedAt, (DateTime?)null), ct);
            }

            return;
        }

        // Koşullu güncelleme: eşzamanlı iki satış eşiği birlikte geçse de tek bildirim gider.
        var now = clock.GetUtcNow().UtcDateTime;
        var marked = await db.Products.Where(p => p.Id == productId && p.LowStockNotifiedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LowStockNotifiedAt, now), ct);
        if (marked != 1)
        {
            return;
        }

        var unit = Enum.Parse<ProductUnit>(row.Unit);
        var managers = await db.BusinessMemberships
            .Where(m => m.BusinessId == businessId && m.IsActive && RoleSets.Managers.Contains(m.Role))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        foreach (var userId in managers)
        {
            notifications.Notify(userId, NotificationTypes.StockLow, $"Kritik stok: {row.Name}",
                $"{CatalogLabels.Quantity(row.After, unit)} kaldı. Kritik seviye: {CatalogLabels.Quantity(critical, unit)}.",
                "/panel/stok?filtre=kritik");
        }
    }

    /// <summary>İşletmede Stok modülü açık mı (Ayarlar → Modüller).</summary>
    public static async Task<bool> ModuleEnabledAsync(AppDbContext db, Guid businessId, CancellationToken ct) =>
        await db.Businesses.Where(b => b.Id == businessId).Select(b => b.Modules).FirstAsync(ct) is var modules
        && modules.HasFlag(KobiFabrika.Domain.Businesses.BusinessModules.Stock);
}
