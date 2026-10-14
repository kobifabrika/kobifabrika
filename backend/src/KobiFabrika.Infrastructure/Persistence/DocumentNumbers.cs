using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Persistence;

/// <summary>
/// İşletme ve yıl bazında sıradaki belge numarası (SAT-2026-000001, TKL-2026-0001). Belgeyle aynı transaction'da çağrılmalı:
/// belge kaydedilemezse numara da geri alınır, eşzamanlı iki satış aynı numarayı alamaz.
/// Sayaç önce <c>UPDATE … WITH (UPDLOCK, SERIALIZABLE)</c> ile artırılır, satır yoksa eklenir. UPDLOCK'lu aralık kilidi
/// eşzamanlı isteklerin birbirini beklemesini sağlar; yılın ilk numarasında satır henüz yokken <c>MERGE … HOLDLOCK</c>
/// paralel isteklerde kilitlenmeye (deadlock 1205) yol açıyordu.
/// </summary>
public sealed class DocumentNumbers(AppDbContext db, ITenantContext tenant)
{
    public async Task<(int Year, int Seq, string No)> NextAsync(DocumentKind kind, DateTime dateUtc, CancellationToken ct)
    {
        var year = DocumentNumber.YearOf(dateUtc);
        var seq = await NextValueAsync(kind, year, ct);
        return (year, seq, DocumentNumber.Format(kind, year, seq));
    }

    /// <summary>Yıldan bağımsız sayaçlar için (ör. "Silinmiş müşteri #N", yıl 0).</summary>
    public async Task<int> NextValueAsync(DocumentKind kind, int year, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Belge numarası transaction içinde alınmalı.");
        }

        var businessId = tenant.BusinessId ?? throw new TenantViolationException("Kiracı bağlamı olmadan belge numarası alınamaz.");
        var kindName = kind.ToString();
        var id = Guid.CreateVersion7();

        var values = await db.Database.SqlQuery<int>($"""
            DECLARE @next int;
            UPDATE [DocumentSequences] WITH (UPDLOCK, SERIALIZABLE)
            SET @next = LastValue = LastValue + 1
            WHERE BusinessId = {businessId} AND Kind = {kindName} AND [Year] = {year};
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO [DocumentSequences] (Id, BusinessId, Kind, [Year], LastValue) VALUES ({id}, {businessId}, {kindName}, {year}, 1);
                SET @next = 1;
            END
            SELECT @next AS [Value];
            """).ToListAsync(ct);

        return values.Single();
    }
}
