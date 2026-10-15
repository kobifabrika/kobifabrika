using KobiFabrika.Domain.Common;

namespace KobiFabrika.Domain.Auditing;

/// <summary>Aktivite günlüğüne düşen işlemler. Arayüzde Türkçe etiketlerle gösterilir.</summary>
public enum AuditAction
{
    Login,
    InvitationSent,
    InvitationResent,
    InvitationRevoked,
    InvitationAccepted,
    RoleChanged,
    MemberRemoved,
    BusinessUpdated,
    SlugChanged,
    WorkingHoursUpdated,
    LogoChanged,
    ModulesChanged,
    PaymentInfoChanged,
    ReceiptSettingsChanged,
    ContactCreated,
    ContactUpdated,
    ContactArchived,
    ContactRestored,
    ContactDeleted,
    ContactsImported,
    ContactsExported,
    LedgerEntryCreated,
    LedgerEntryDeleted,
    OpeningBalanceSet,
    CollectionRecorded,
    CollectionReverted,
    PaymentRequestCreated,
    PaymentRequestConfirmed,
    PaymentRequestRejected,
    PaymentRequestCancelled,
    PaymentRequestRenewed,
    StatementShared,
    ProductCreated,
    ProductUpdated,
    ProductDeleted,
    ProductsImported,
    ProductsExported,
    StockMovementAdded,
    SaleDiscounted,
    SaleCancelled,
    SalesExported,
    QuoteCreated,
    QuoteUpdated,
    QuoteShared,
    QuoteRenewed,

    /// <summary>Süresi dolmuş teklifin geçerliliği kullanıcı onayıyla uzatıldı.</summary>
    QuoteExtended,
    QuoteAccepted,
    QuoteRejected,
    QuoteConverted,
    QuoteDeleted,
    CashAccountCreated,
    CashAccountUpdated,
    CashAccountDeactivated,
    CashAccountActivated,
    DefaultCashAccountChanged,
    CashTransferRecorded,
    CashTransactionReversed,
    SupplierPaymentRecorded,
    ExpenseRecorded,
    IncomeRecorded,
    ExpenseCategoryCreated,
    ExpenseCategoryUpdated,
    ExpenseCategoryDeactivated,
    ExpenseCategoryActivated,
    DueReminderSent,
    FinanceSettingsChanged,
    ReportExported,
    CashTransactionsExported,
    StaffCreated,
    StaffUpdated,
    StaffDeleted,
    StaffWorkingHoursUpdated,
    StaffServicesUpdated,
    HolidayCreated,
    HolidayDeleted,
    AppointmentSettingsChanged,
    BookingSettingsChanged,
    ServicePackageCreated,
    ServicePackageUpdated,
    ServicePackageDeleted,
    CustomerPackageExtended,
    CustomerPackageClosed,
    SmsCreditsAdded,
    SmsCreditsExported,
    SmsSettingsChanged,
    CampaignCreated,
    CampaignCancelled,
    ContactDataExported,

    /// <summary>KVKK: müşterinin kişisel verileri silindi (anonimleştirildi). Kayıtta kişisel veri tutulmaz.</summary>
    ContactAnonymized,
    AutomationChanged,
    ReviewSettingsChanged,

    /// <summary>Maaş değişti (tutar yazılmaz; Yönetici günlüğü görür).</summary>
    StaffSalaryChanged,
    TimeEntryAdded,
    TimeEntryCorrected,
    TimeEntryDeleted,
    LeaveRequested,
    LeaveApproved,
    LeaveRejected,
    LeaveCancelled,
    LeaveBalanceAdjusted,
    LeaveTypeChanged,
    StaffSettingsChanged,
}

/// <summary>
/// Aktivite günlüğü kaydı. Sadece eklenir; güncellenemez ve silinemez (AppDbContext engeller).
/// Soft delete ve audit alanları yoktur; zaman <see cref="At"/>'tedir.
/// </summary>
public class AuditLog : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid? UserId { get; set; }
    public AuditAction Action { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>Değişen alanlar veya ayrıntılar (JSON).</summary>
    public string? Changes { get; set; }

    public string? IpAddress { get; set; }
    public DateTime At { get; set; }
}
