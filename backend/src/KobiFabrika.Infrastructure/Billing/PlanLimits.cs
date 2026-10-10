using KobiFabrika.Domain.Businesses;
using KobiFabrika.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Billing;

/// <summary>
/// Paket limitleri. Sprint 1'de limit uygulanmaz (sınırsız); sadece yeri ve sayım kuralı hazır.
/// Sprint 8'de Plans/Subscriptions tablosundan gerçek limit okunacak.
/// </summary>
public interface IPlanLimits
{
    /// <summary>Bu role sahip yeni bir kullanıcı (üye veya bekleyen davet) eklenebilir mi?</summary>
    Task<bool> CanAddUserAsync(BusinessRole role, CancellationToken cancellationToken = default);

    /// <summary>Bu kadar yeni müşteri/tedarikçi eklenebilir mi? (Sprint 8'e kadar sınırsız.)</summary>
    Task<bool> CanAddContactsAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Kullanıcı sınırına sayılanlar: aktif üyeler + bekleyen davetler; Mali Müşavir hariç.</summary>
    Task<int> CountBillableUsersAsync(CancellationToken cancellationToken = default);
}

internal sealed class PlanLimits(AppDbContext db, TimeProvider clock) : IPlanLimits
{
    /// <summary>Null = sınırsız.</summary>
    private static int? MaxUsers => null;

    private static int? MaxContacts => null;

    public async Task<bool> CanAddContactsAsync(int count, CancellationToken cancellationToken = default)
    {
        if (MaxContacts is not { } max)
        {
            return true;
        }

        return await db.Contacts.CountAsync(cancellationToken) + count <= max;
    }

    public async Task<bool> CanAddUserAsync(BusinessRole role, CancellationToken cancellationToken = default)
    {
        if (role == BusinessRole.Accountant || MaxUsers is not { } max)
        {
            return true;
        }

        return await CountBillableUsersAsync(cancellationToken) < max;
    }

    public async Task<int> CountBillableUsersAsync(CancellationToken cancellationToken = default)
    {
        var businessId = db.CurrentBusinessId;
        var now = clock.GetUtcNow().UtcDateTime;

        var members = await db.BusinessMemberships
            .CountAsync(m => m.BusinessId == businessId && m.IsActive && m.Role != BusinessRole.Accountant, cancellationToken);
        var pending = await db.Invitations
            .CountAsync(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now && i.Role != BusinessRole.Accountant, cancellationToken);

        return members + pending;
    }
}
