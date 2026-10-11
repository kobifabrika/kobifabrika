using KobiFabrika.Domain.Common;
using KobiFabrika.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KobiFabrika.Infrastructure.Persistence;

/// <summary>
/// İşletme ve yıl bazında sıradaki belge numarası (SAT-2026-000001, TKL-2026-0001). Sayaç satırı tek bir
/// <c>MERGE … WITH (HOLDLOCK) … OUTPUT</c> ile artırılır veya açılır; belgeyle aynı transaction'da çağrılmalı:
/// belge kaydedilemezse numara da geri alınır, eşzamanlı iki satış aynı numarayı alamaz.
/// </summary>
public sealed class DocumentNumbers(AppDbContext db, ITenantContext tenant)
{
    public async Task<(int Year, int Seq, string No)> NextAsync(DocumentKind kind, DateTime dateUtc, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Belge numarası transaction içinde alınmalı.");
        }

        var businessId = tenant.BusinessId ?? throw new TenantViolationException("Kiracı bağlamı olmadan belge numarası alınamaz.");
        var year = DocumentNumber.YearOf(dateUtc);
        var kindName = kind.ToString();
        var id = Guid.CreateVersion7();

        var values = await db.Database.SqlQuery<int>($"""
            MERGE [DocumentSequences] WITH (HOLDLOCK) AS t
            USING (SELECT {businessId} AS BusinessId, {kindName} AS Kind, {year} AS [Year]) AS s
            ON t.BusinessId = s.BusinessId AND t.Kind = s.Kind AND t.[Year] = s.[Year]
            WHEN MATCHED THEN UPDATE SET LastValue = t.LastValue + 1
            WHEN NOT MATCHED THEN INSERT (Id, BusinessId, Kind, [Year], LastValue) VALUES ({id}, s.BusinessId, s.Kind, s.[Year], 1)
            OUTPUT inserted.LastValue AS [Value];
            """).ToListAsync(ct);

        var seq = values.Single();
        return (year, seq, DocumentNumber.Format(kind, year, seq));
    }
}
