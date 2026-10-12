using FluentValidation;
using KobiFabrika.Api.Common;
using KobiFabrika.Api.Common.Authorization;
using KobiFabrika.Domain.Businesses;
using KobiFabrika.Domain.Common;
using KobiFabrika.Domain.Finance;

namespace KobiFabrika.Api.Features.Finance;

public sealed record CashAccountResponse(
    Guid Id, string Name, CashAccountType Type, decimal Balance, bool IsDefault, string? Iban, string? IbanHolderName, bool IsActive);

/// <summary>Satış ekranı için hesap seçenekleri (bakiyesiz; her rol görür).</summary>
public sealed record CashAccountOption(Guid Id, string Name, CashAccountType Type, bool IsDefault);

/// <summary>OpeningBalance: hesap açılırken bakiye (eksi olabilir: KMH'li banka); sonradan değişmez, düzeltme ters kayıtla.</summary>
public sealed record CashAccountCreateRequest(
    string? Name, CashAccountType? Type, string? Iban, string? IbanHolderName, decimal? OpeningBalance, DateOnly? OpeningDate);

public sealed record CashAccountUpdateRequest(string? Name, string? Iban, string? IbanHolderName);

public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal? Amount, DateOnly? Date, string? Description);

public sealed record TransferResponse(Guid TransferId, decimal FromBalance, decimal ToBalance);

public sealed record ContactPaymentRequest(Guid CashAccountId, decimal? Amount, DateOnly? Date, string? Description);

/// <summary>AccountBalance: işlemden sonra hesap bakiyesi (eksiye düştüyse ekranda uyarı).</summary>
public sealed record ContactPaymentResponse(Guid CashTransactionId, decimal ContactBalance, decimal AccountBalance);

public sealed record ReverseRequest(string? Reason);

public sealed record ReverseResponse(IReadOnlyList<Guid> ReversalIds, IReadOnlyList<AccountBalance> Balances);

public sealed record AccountBalance(Guid AccountId, decimal Balance);

/// <summary>
/// Hesap hareketi. NetAmount: bakiyeye etkisi (giriş +, çıkış −; ters kayıtta ters yön). ReversalOf: bu satır ters kayıtsa geri aldığı
/// hareket; ReversedBy: bu satırın ters kaydı. Maaş giderinde Yönetici açıklamayı göremez (Description "Personel gideri").
/// </summary>
public sealed record CashTransactionItem(
    Guid Id, DateOnly Date, CashTransactionType Type, string TypeLabel, decimal Amount, decimal NetAmount, decimal Balance, string Description,
    Guid? ContactId, string? ContactName, Guid? CategoryId, string? CategoryName, string? DocumentNo, string? CounterAccountName,
    bool IsReversal, ReversalLink? ReversalOf, ReversalLink? ReversedBy, Guid? SaleId, bool CanReverse, DateTime CreatedAt);

public sealed record ReversalLink(Guid Id, DateOnly Date);

/// <summary>InTotal/OutTotal: aralıktaki giriş ve çıkış toplamı; ters kayıtlar kendi türünün toplamından düşülür.</summary>
public sealed record CashTransactionsResponse(
    CashAccountResponse Account, DateOnly From, DateOnly To, decimal OpeningBalance, decimal InTotal, decimal OutTotal, decimal ClosingBalance,
    IReadOnlyList<CashTransactionItem> Items, int Total);

public static class FinanceVisibility
{
    /// <summary>Maaş giderinin ayrıntısını (açıklama, kategori, belge no) Sahip ve Mali Müşavir görür; Yönetici sadece toplamı.</summary>
    public static bool ShowSalaryDetail(CurrentMember member) => member.Role is BusinessRole.Owner or BusinessRole.Accountant;

    public const string SalaryMaskedDescription = "Personel gideri";
    public const string SalaryMaskedCategory = "Personel giderleri";
}

public sealed class CashAccountCreateRequestValidator : AbstractValidator<CashAccountCreateRequest>
{
    public CashAccountCreateRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Hesap adı girin.")
            .MaximumLength(CashAccount.MaxNameLength).WithMessage("Hesap adı en fazla 100 karakter olabilir.");
        RuleFor(x => x.Type).NotNull().WithMessage("Hesap türünü seçin.").IsInEnum().WithMessage("Hesap türünü seçin.");
        RuleFor(x => x.Iban).ValidIban().When(x => !string.IsNullOrWhiteSpace(x.Iban));
        RuleFor(x => x.IbanHolderName).MaximumLength(100).WithMessage("Hesap sahibi en fazla 100 karakter olabilir.");
        RuleFor(x => x.OpeningBalance)
            .Must(v => v is null || v == 0 || Money.Validate(Math.Abs(v.Value)) is null)
            .WithMessage((_, v) => Money.Validate(Math.Abs(v!.Value)) ?? "");
        RuleFor(x => x.OpeningDate).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
    }
}

public sealed class CashAccountUpdateRequestValidator : AbstractValidator<CashAccountUpdateRequest>
{
    public CashAccountUpdateRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Hesap adı girin.")
            .MaximumLength(CashAccount.MaxNameLength).WithMessage("Hesap adı en fazla 100 karakter olabilir.");
        RuleFor(x => x.Iban).ValidIban().When(x => !string.IsNullOrWhiteSpace(x.Iban));
        RuleFor(x => x.IbanHolderName).MaximumLength(100).WithMessage("Hesap sahibi en fazla 100 karakter olabilir.");
    }
}

public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.FromAccountId).NotEmpty().WithMessage("Paranın çıkacağı hesabı seçin.");
        RuleFor(x => x.ToAccountId).NotEmpty().WithMessage("Paranın gireceği hesabı seçin.")
            .Must((x, to) => to != x.FromAccountId).WithMessage("Aynı hesaba transfer yapılamaz.");
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Date).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
    }
}

public sealed class ContactPaymentRequestValidator : AbstractValidator<ContactPaymentRequest>
{
    public ContactPaymentRequestValidator()
    {
        RuleFor(x => x.CashAccountId).NotEmpty().WithMessage("Kasa veya banka hesabı seçin.");
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Date).Must(FinanceRules.ValidDate).WithMessage(FinanceRules.DateMessage);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
    }
}

public sealed class ReverseRequestValidator : AbstractValidator<ReverseRequest>
{
    public ReverseRequestValidator() =>
        RuleFor(x => x.Reason).MaximumLength(200).WithMessage("Neden en fazla 200 karakter olabilir.");
}
