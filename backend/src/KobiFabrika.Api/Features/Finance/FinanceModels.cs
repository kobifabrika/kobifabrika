using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Features.Customers;
using KobiFabrika.Domain.Finance;

namespace KobiFabrika.Api.Features.Finance;

public enum LedgerKind
{
    /// <summary>Borç: müşteri bize borçlanır (bakiye artar).</summary>
    Debit,

    /// <summary>Alacak: borç kapanır veya biz borçlanırız (bakiye azalır).</summary>
    Credit,
}

public sealed record LedgerEntryRequest(LedgerKind Kind, decimal? Amount, DateOnly? Date, DateOnly? DueDate, string? Description);

public sealed record CollectionRequest(decimal? Amount, DateOnly? Date, Guid CashAccountId, string? Description);

/// <summary>Cari hareket satırı. <see cref="Balance"/>: bu satırdan sonraki yürüyen bakiye (pozitif = Alacak).</summary>
public sealed record LedgerItem(
    Guid Id, DateOnly Date, DateOnly? DueDate, string Description, decimal Debit, decimal Credit, decimal Balance,
    LedgerRefType RefType, Guid? RefId, DateTime CreatedAt);

/// <summary>Tarih aralığındaki ekstre: devreden bakiye + hareketler + aralık sonu bakiyesi.</summary>
public sealed record LedgerResponse(
    Guid ContactId, DateOnly? From, DateOnly To, decimal CarriedBalance, IReadOnlyList<LedgerItem> Items,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance, decimal CurrentBalance);

public sealed record CashAccountResponse(Guid Id, string Name, CashAccountType Type, decimal Balance, bool IsDefault);

public sealed record CollectionResponse(Guid CashTransactionId, Guid LedgerEntryId, decimal ContactBalance);

public sealed class LedgerEntryRequestValidator : AbstractValidator<LedgerEntryRequest>
{
    public LedgerEntryRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Borç mu alacak mı seçin.");
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Date).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
        RuleFor(x => x.DueDate).Must((x, d) => d is null || d >= (x.Date ?? DateOnly.MinValue))
            .WithMessage("Vade tarihi hareket tarihinden önce olamaz.");
        RuleFor(x => x.Description).Must(d => !string.IsNullOrWhiteSpace(d)).WithMessage("Açıklama girin.")
            .MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
    }
}

public sealed class CollectionRequestValidator : AbstractValidator<CollectionRequest>
{
    public CollectionRequestValidator()
    {
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Date).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
        RuleFor(x => x.CashAccountId).NotEmpty().WithMessage("Kasa veya banka hesabı seçin.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
    }
}

public sealed class OpeningBalanceRequestValidator : AbstractValidator<OpeningBalanceRequest>
{
    public OpeningBalanceRequestValidator()
    {
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Direction).IsInEnum().WithMessage("Bakiyenin yönünü seçin.");
        RuleFor(x => x.Date).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
    }
}

public static class FinanceRules
{
    public const string DateMessage = "Geçerli bir tarih girin.";

    /// <summary>Boş = bugün. 2000'den önce veya 1 yıldan ileri tarih kabul edilmez.</summary>
    public static bool ValidDate(DateOnly? date) =>
        date is null || (date.Value.Year >= 2000 && date.Value <= DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)));
}
