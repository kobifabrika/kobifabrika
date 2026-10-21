using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Marketing;
using KobiFabrika.Infrastructure.Notifications;
using KobiFabrika.Infrastructure.Persistence;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Messaging;

/// <summary>
/// İşletmenin SMS kredisi. Bakiye (<c>Businesses.SmsBalance</c>) sadece buradaki atomik UPDATE ile değişir:
/// düşüm <c>WHERE SmsBalance &gt;= @n</c> şartlı olduğu için eşzamanlı gönderimde bakiye eksiye düşmez (ayrıca CHECK).
/// Her değişiklik aynı transaction'da SmsCreditTransactions'a yazılır (Σ hareket = bakiye). İşletme satırı sadece bu kısa
/// transaction'da kilitlenir; başka kilitlerle birlikte tutulmaz (SMS'ler randevu/satış commit'inden sonra gönderilir).
/// Kiracı bağlamında çağrılır.
/// </summary>
public sealed class SmsCredits(AppDbContext db, ITenantContext tenant, INotificationService notifications, TimeProvider clock)
{
    public const string LowBalanceType = "sms.low_balance";
    public const string OutOfCreditType = "sms.out_of_credit";
    public const string SettingsLink = "/panel/ayarlar/sms";

    private Guid BusinessId => tenant.BusinessId ?? throw new TenantViolationException("Kiracı bağlamı olmadan SMS kredisi değişemez.");

    /// <summary>Bakiyeden <paramref name="amount"/> düşer; yetmezse null (hiçbir şey değişmez). Açık transaction içinde çağrılmalı.</summary>
    internal async Task<int?> TryDebitAsync(int amount, CancellationToken ct)
    {
        var businessId = BusinessId;
        var rows = await db.Database.SqlQuery<int>($"""
            UPDATE [Businesses] SET SmsBalance = SmsBalance - {amount}
            OUTPUT inserted.SmsBalance AS [Value]
            WHERE Id = {businessId} AND SmsBalance >= {amount}
            """).ToListAsync(ct);
        return rows.Count == 1 ? rows[0] : null;
    }

    /// <summary>Bakiyeye ekler (yükleme, iade). Açık transaction içinde çağrılmalı.</summary>
    internal async Task<int> CreditAsync(int amount, CancellationToken ct)
    {
        var businessId = BusinessId;
        var rows = await db.Database.SqlQuery<int>($"""
            UPDATE [Businesses] SET SmsBalance = SmsBalance + {amount}
            OUTPUT inserted.SmsBalance AS [Value]
            WHERE Id = {businessId}
            """).ToListAsync(ct);
        return rows.Single();
    }

    public async Task<int> BalanceAsync(CancellationToken ct)
    {
        var businessId = BusinessId;
        return await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.SmsBalance).FirstAsync(ct);
    }

    /// <summary>
    /// Kredi yükler (platform; Sprint 8'de ödeme). Uyarı bayrakları sıfırlanır: bakiye tekrar düşünce yine bildirim gider.
    /// </summary>
    public async Task<int> TopUpAsync(int amount, SmsCreditKind kind, string? note, CancellationToken ct)
    {
        if (amount <= 0 || amount > SmsCredit.MaxTopUp)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var balance = await CreditAsync(amount, ct);
        db.SmsCreditTransactions.Add(new SmsCreditTransaction { Amount = amount, Kind = kind, BalanceAfter = balance, Note = note });
        await db.SaveChangesAsync(ct);
        var businessId = BusinessId;
        await db.Businesses.Where(b => b.Id == businessId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.SmsLowBalanceNotifiedAt, (DateTime?)null)
            .SetProperty(b => b.SmsOutOfCreditNotifiedAt, (DateTime?)null), ct);
        await tx.CommitAsync(ct);
        return balance;
    }

    /// <summary>Hoş geldin kredisi: işletme başına bir kez (filtreli benzersiz indeks). Verildiyse true.</summary>
    public async Task<bool> GrantWelcomeAsync(CancellationToken ct)
    {
        var businessId = BusinessId;
        if (await db.SmsCreditTransactions.AnyAsync(t => t.BusinessId == businessId && t.Kind == SmsCreditKind.Welcome, ct))
        {
            return false;
        }

        try
        {
            await TopUpAsync(SmsCredit.WelcomeCredits, SmsCreditKind.Welcome, "Hoş geldiniz kredisi", ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Düşümden sonra: eşiğin altına inildiyse bir kez düşük bakiye bildirimi.</summary>
    internal async Task AfterDebitAsync(int before, int after, CancellationToken ct)
    {
        var businessId = BusinessId;
        var threshold = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.SmsLowBalanceThreshold).FirstAsync(ct);
        if (threshold <= 0 || after >= threshold)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var marked = await db.Businesses.Where(b => b.Id == businessId && b.SmsLowBalanceNotifiedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.SmsLowBalanceNotifiedAt, now), ct);
        if (marked == 1)
        {
            await NotifyManagersAsync(LowBalanceType, $"SMS krediniz azaldı: {after} kredi kaldı",
                "Kredi bitince randevu hatırlatmaları ve kampanyalar gönderilmez.", ct);
        }
    }

    /// <summary>Kredi yetmedi: bir kez "kredi bitti" bildirimi (yüklemeye kadar tekrarlanmaz).</summary>
    internal async Task OutOfCreditAsync(CancellationToken ct)
    {
        var businessId = BusinessId;
        var now = clock.GetUtcNow().UtcDateTime;
        var marked = await db.Businesses.Where(b => b.Id == businessId && b.SmsOutOfCreditNotifiedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.SmsOutOfCreditNotifiedAt, now), ct);
        if (marked == 1)
        {
            await NotifyManagersAsync(OutOfCreditType, "Krediniz bitti, SMS'ler gitmiyor",
                "Randevu hatırlatmaları ve diğer SMS'ler kredi yüklenene kadar gönderilmez.", ct);
        }
    }

    private async Task NotifyManagersAsync(string type, string title, string body, CancellationToken ct)
    {
        var businessId = BusinessId;
        var users = await db.BusinessMemberships.AsNoTracking()
            .Where(m => m.BusinessId == businessId && m.IsActive && (m.Role == BusinessRole.Owner || m.Role == BusinessRole.Manager))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        foreach (var userId in users)
        {
            notifications.Notify(userId, type, title, body, SettingsLink);
        }

        await db.SaveChangesAsync(ct);
    }
}
