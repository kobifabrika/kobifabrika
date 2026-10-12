using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Finance;

public enum ExpenseCategoryKind
{
    Expense,
    Income,
}

/// <summary>
/// Gelir-gider kategorisi. Varsayılanlar kurulumda (mevcut işletmelere migration'da) eklenir. Kullanılan kategori silinmez,
/// pasife alınır. <see cref="SystemKey"/> adı değişse de kuralı korur (ör. maaş giderlerini sadece Sahip girer/ayrıntısını görür).
/// </summary>
public class ExpenseCategory : TenantEntity
{
    public const int MaxNameLength = 100;
    public const string SalaryKey = "salary";

    public required string Name { get; set; }
    public ExpenseCategoryKind Kind { get; set; }
    public string? SystemKey { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public bool IsSalary => SystemKey == SalaryKey;

    /// <summary>(Ad, tür, sistem anahtarı, sıra). Migration'daki backfill SQL'i aynı listeyi kullanır.</summary>
    public static readonly IReadOnlyList<(string Name, ExpenseCategoryKind Kind, string? SystemKey, int SortOrder)> DefaultList =
    [
        ("Kira", ExpenseCategoryKind.Expense, null, 1),
        ("Elektrik-Su-Doğalgaz", ExpenseCategoryKind.Expense, null, 2),
        ("İnternet-Telefon", ExpenseCategoryKind.Expense, null, 3),
        ("Maaş", ExpenseCategoryKind.Expense, SalaryKey, 4),
        ("Malzeme", ExpenseCategoryKind.Expense, null, 5),
        ("Vergi-SGK", ExpenseCategoryKind.Expense, null, 6),
        ("Diğer", ExpenseCategoryKind.Expense, null, 99),
        ("Diğer gelir", ExpenseCategoryKind.Income, null, 99),
    ];

    public static List<ExpenseCategory> Defaults() =>
        DefaultList.Select(d => new ExpenseCategory { Name = d.Name, Kind = d.Kind, SystemKey = d.SystemKey, SortOrder = d.SortOrder }).ToList();
}
