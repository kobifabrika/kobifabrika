import type { components } from "./schema";

type Schemas = components["schemas"];

export type Me = Schemas["MeResponse"];
export type MeBusiness = NonNullable<Me["business"]>;
export type Role = "Owner" | "Manager" | "Staff" | "Accountant";
export type Sector = Schemas["SectorResponse"]["sector"];
export type WorkingHour = Schemas["WorkingHourDto"];
export type Business = Schemas["BusinessResponse"];
export type Member = Schemas["MemberResponse"];
export type Invitation = Schemas["InvitationResponse"];
export type NotificationItem = Schemas["NotificationResponse"];
export type AuditLogItem = Schemas["AuditLogResponse"];
export type AuditAction = Schemas["AuditLogResponse"]["action"];
export type City = Schemas["City"];

// Müşteriler ve cari (Sprint 2)
export type ContactListItem = Schemas["ContactListItem"];
export type Contact = Schemas["ContactResponse"];
export type ContactRequest = Schemas["ContactRequest"];
export type ContactSource = NonNullable<Schemas["ContactSource"]>;
export type ConsentChannel = Schemas["ConsentChannel"];
export type Tag = Schemas["TagDto"];
export type TagListItem = Schemas["TagListItem"];
export type TagColor = Schemas["TagColor"];
export type Segment = Schemas["SegmentResponse"];
export type ContactFilter = Schemas["ContactFilter"];
export type TimelineItem = Schemas["TimelineItem"];
export type Note = Schemas["NoteResponse"];
export type Reminder = Schemas["ReminderResponse"];
export type Ledger = Schemas["LedgerResponse"];
export type LedgerItem = Schemas["LedgerItem"];
export type CashAccount = Schemas["CashAccountResponse"];
export type PaymentRequest = Schemas["PaymentRequestResponse"];
export type PaymentRequestStatus = Schemas["PaymentRequestStatus"];
export type ImportField = Schemas["ImportFieldInfo"];
export type ImportRow = Schemas["ImportRowResult"];
export type PublicStatement = Schemas["PublicStatementResponse"];
export type PublicPayment = Schemas["PublicPaymentResponse"];

// Ürün, stok, satış ve teklif (Sprint 3)
export type Product = Schemas["ProductResponse"];
export type ProductRequest = Schemas["ProductRequest"];
export type ProductType = Schemas["ProductType"];
export type ProductUnit = Schemas["ProductUnit"];
export type Category = Schemas["CategoryResponse"];
export type VatRate = Schemas["VatRateResponse"];
export type StockMovement = Schemas["StockMovementResponse"];
export type StockMovementType = Schemas["StockMovementType"];
export type Sale = Schemas["SaleResponse"];
export type SaleListItem = Schemas["SaleListItem"];
export type SaleStatus = Schemas["SaleStatus"];
export type PaymentMethod = Schemas["PaymentMethod"];
export type DocumentLine = Schemas["DocumentLineResponse"];
export type DocumentLineRequest = Schemas["DocumentLineRequest"];
export type Quote = Schemas["QuoteResponse"];
export type QuoteListItem = Schemas["QuoteListItem"];
export type QuoteStatus = Schemas["QuoteStatus"];
export type PublicQuote = Schemas["PublicQuoteResponse"];

// Finans, Bugün ve raporlar (Sprint 4)
export type CashAccountOption = Schemas["CashAccountOption"];
export type CashAccountType = Schemas["CashAccountType"];
export type CashTransactionItem = Schemas["CashTransactionItem"];
export type CashTransactions = Schemas["CashTransactionsResponse"];
export type CashTransactionType = Schemas["CashTransactionType"];
export type ExpenseCategory = Schemas["ExpenseCategoryResponse"];
export type ExpenseCategoryKind = Schemas["ExpenseCategoryKind"];
export type ExpenseItem = Schemas["ExpenseItem"];
export type ReceivableTab = Schemas["ReceivableTab"];
export type ReceivableRow = Schemas["ReceivableRow"];
export type ReceivableList = Schemas["ReceivableListResponse"];
export type ContactReceivable = Schemas["ContactReceivableResponse"];
export type ReminderMessage = Schemas["ReminderMessageResponse"];
export type Dashboard = Schemas["DashboardResponse"];
export type SalesReport = Schemas["SalesReportResponse"];
export type TopProductsReport = Schemas["TopProductsResponse"];
export type ReceivablesReport = Schemas["ReceivablesReportResponse"];
export type IncomeExpenseReport = Schemas["IncomeExpenseResponse"];
export type ReportGroupBy = Schemas["ReportGroupBy"];

// Personel ve randevu (Sprint 5)
export type Staff = Schemas["StaffResponse"];
export type StaffRequest = Schemas["StaffRequest"];
export type StaffColor = Schemas["StaffColor"];
export type StaffWorkingHours = Schemas["StaffWorkingHoursDto"];
export type StaffDay = Schemas["StaffDayDto"];
export type TimeRange = Schemas["TimeRangeDto"];
export type TimeOff = Schemas["TimeOffResponse"];
export type TimeOffKind = Schemas["TimeOffKind"];
export type Holiday = Schemas["HolidayResponse"];
export type Appointment = Schemas["AppointmentResponse"];
export type AppointmentStatus = Schemas["AppointmentStatus"];
export type AppointmentSaved = Schemas["AppointmentSavedResponse"];
export type AppointmentCheck = Schemas["AppointmentCheckResponse"];
export type AppointmentSaleDraft = Schemas["AppointmentSaleDraft"];
export type Calendar = Schemas["CalendarResponse"];
export type CalendarStaff = Schemas["CalendarStaff"];
export type CalendarDay = Schemas["CalendarDay"];
export type ContactAppointments = Schemas["ContactAppointmentsResponse"];
export type NoShowWarning = Schemas["NoShowWarning"];
export type DashboardAppointments = Schemas["DashboardAppointments"];

// Online randevu, SMS ve paketler (Sprint 5B)
export type BookingSettings = Schemas["BookingSettingsResponse"];
export type BookingSettingsRequest = Schemas["BookingSettingsRequest"];
export type PublicBooking = Schemas["PublicBookingResponse"];
export type PublicBookingService = Schemas["PublicBookingService"];
export type PublicBookingStaff = Schemas["PublicBookingStaff"];
export type PublicSlots = Schemas["PublicSlotsResponse"];
export type PublicBookingCreated = Schemas["PublicBookingCreated"];
export type PublicAppointment = Schemas["PublicAppointmentResponse"];
export type ServicePackage = Schemas["ServicePackageResponse"];
export type ServicePackageRequest = Schemas["ServicePackageRequest"];
export type CustomerPackage = Schemas["CustomerPackageResponse"];
export type CustomerPackageState = Schemas["CustomerPackageState"];
export type ContactPackages = Schemas["ContactPackagesResponse"];
export type MessageLogItem = Schemas["MessageLogItem"];
export type MessagePurpose = Schemas["MessagePurpose"];

export const packageStateNames: Record<CustomerPackageState, string> = {
  Active: "Aktif",
  Finished: "Bitti",
  Expired: "Süresi doldu",
  Cancelled: "İptal",
};

export const messagePurposeNames: Record<MessagePurpose, string> = {
  Verification: "Doğrulama kodu",
  AppointmentConfirmation: "Randevu onayı",
  AppointmentReminder: "Randevu hatırlatması",
  AppointmentMoved: "Randevu değişikliği",
  AppointmentCancelled: "Randevu iptali",
  AppointmentRejected: "Randevu reddi",
  Campaign: "Kampanya",
  Birthday: "Doğum günü",
  WinBack: "Geri kazanma",
  VisitThanks: "Ziyaret sonrası teşekkür",
  Survey: "Memnuniyet anketi",
  DueReminder: "Vade hatırlatması",
};

export const staffColorNames: Record<StaffColor, string> = {
  Black: "Siyah",
  DarkGray: "Koyu gri",
  Gray: "Gri",
  LightGray: "Açık gri",
  Blue: "Mavi",
  Outline: "Çerçeveli",
};

export const timeOffKindNames: Record<TimeOffKind, string> = { Break: "Mola", Leave: "İzin", Other: "Kapalı" };

export const appointmentStatusNames: Record<AppointmentStatus, string> = {
  Pending: "Beklemede",
  Confirmed: "Onaylı",
  Completed: "Tamamlandı",
  NoShow: "Gelmedi",
  Cancelled: "İptal",
};

export const cashAccountTypeNames: Record<CashAccountType, string> = { Cash: "Nakit", Bank: "Banka" };

/** Kaynak listesi sabit; sıra arayüzdeki sıradır. */
export const sourceNames: Record<ContactSource, string> = {
  Instagram: "Instagram",
  Google: "Google",
  Referral: "Tavsiye",
  WalkIn: "Yoldan geçerken",
  Facebook: "Facebook",
  TikTok: "TikTok",
  Other: "Diğer",
  OnlineBooking: "Online randevu",
};

export const consentNames: Record<ConsentChannel, string> = { Sms: "SMS", Email: "E-posta", Call: "Arama" };

export const consentSourceNames: Record<NonNullable<Schemas["ConsentSource"]>, string> = {
  Panel: "panelden",
  Import: "Excel içe aktarmadan",
  OnlineBooking: "online randevudan",
  OptOutLink: "çıkış linkinden",
  OptOutSms: "RET SMS'iyle",
  Iys: "İYS'den",
  Anonymization: "kişisel veriler silinirken",
};

export const tagColorNames: Record<TagColor, string> = {
  Gray: "Gri",
  Black: "Siyah",
  Blue: "Mavi",
  Outline: "Çerçeveli",
};

export const paymentStatusNames: Record<PaymentRequestStatus, string> = {
  Open: "Bekliyor",
  ClaimedPaid: '"Ödedim" dedi',
  Confirmed: "Onaylandı",
  Cancelled: "İptal",
};

export const productTypeNames: Record<ProductType, string> = { Product: "Ürün", Service: "Hizmet" };

export const paymentMethodNames: Record<PaymentMethod, string> = {
  Cash: "Nakit",
  Card: "Kart",
  Transfer: "Havale/EFT",
  Credit: "Veresiye",
};

export const stockMovementTypeNames: Record<StockMovementType, string> = {
  In: "Giriş",
  Out: "Çıkış",
  Adjustment: "Sayım",
  Sale: "Satış",
  SaleCancel: "Satış iptali",
};

export const quoteStatusNames: Record<QuoteStatus, string> = {
  Draft: "Taslak",
  Sent: "Gönderildi",
  Accepted: "Onaylandı",
  Rejected: "Reddedildi",
  Converted: "Satışa dönüştü",
};

export const roleNames: Record<Role, string> = {
  Owner: "Sahip",
  Manager: "Yönetici",
  Staff: "Personel",
  Accountant: "Mali Müşavir",
};

export const roleDescriptions: Record<Role, string> = {
  Owner: "Her şey; maaş ve abonelik dahil.",
  Manager: "Maaş ve abonelik hariç her şey.",
  Staff: "Kendi randevuları, satış girme, müşteri görme.",
  Accountant: "Salt okunur; raporları indirir. Kullanıcı sınırına sayılmaz.",
};

// Pazarlama, SMS kredisi, KVKK (Sprint 6A)
export type SmsCreditSummary = Schemas["SmsCreditSummary"];
export type SmsCreditTransactionItem = Schemas["SmsCreditTransactionItem"];
export type SmsCreditKind = Schemas["SmsCreditKind"];
export type Campaign = Schemas["CampaignResponse"];
export type CampaignStatus = Schemas["CampaignStatus"];
export type CampaignKind = Schemas["CampaignKind"];
export type CampaignTarget = Schemas["CampaignTarget"];
export type CampaignPreview = Schemas["CampaignPreviewResponse"];
export type CampaignComposeInfo = Schemas["CampaignComposeInfo"];
export type CampaignRecipientItem = Schemas["CampaignRecipientItem"];
export type RecipientStatus = Schemas["RecipientStatus"];
/** Yanıtta metin (nullable enum ortak şemayı bozduğu için); değerler backend RecipientSkipReason. */
export type RecipientSkipReason =
  "NoPhone" | "NoConsent" | "OptedOut" | "DailyLimit" | "NoCredit" | "Cancelled" | "Unavailable" | "Interrupted";
export type AdminBusinessItem = Schemas["AdminBusinessItem"];
export type PublicOptOut = Schemas["PublicOptOutResponse"];

export const smsCreditKindNames: Record<SmsCreditKind, string> = {
  TopUp: "Yükleme",
  Welcome: "Hoş geldiniz kredisi",
  Send: "Gönderim",
  Refund: "İade",
  Adjustment: "Düzeltme",
};

export const campaignStatusNames: Record<CampaignStatus, string> = {
  Scheduled: "Zamanlandı",
  Sending: "Gönderiliyor",
  Completed: "Tamamlandı",
  Cancelled: "İptal edildi",
};

export const recipientStatusNames: Record<RecipientStatus, string> = {
  Pending: "Sırada",
  Sending: "Gönderiliyor",
  Sent: "Gönderildi",
  Failed: "Başarısız",
  Skipped: "Elendi",
};

export const skipReasonNames: Record<RecipientSkipReason, string> = {
  NoPhone: "cep telefonu yok",
  NoConsent: "SMS izni yok",
  OptedOut: "çıkış yaptı",
  DailyLimit: "bugün SMS aldı",
  NoCredit: "kredi yetersiz",
  Cancelled: "kampanya iptal",
  Unavailable: "kayıt silindi/arşivde",
  Interrupted: "sonuç belirsiz",
};

// Otomasyon, anket, öneriler (Sprint 6B)
export type Automation = Schemas["AutomationResponse"];
export type AutomationType = Schemas["AutomationType"];
export type AutomationPreview = Schemas["AutomationPreview"];
export type SurveyItem = Schemas["SurveyItem"];
export type SurveySummary = Schemas["SurveySummary"];
export type PublicSurvey = Schemas["PublicSurveyResponse"];
export type EmptySlots = Schemas["EmptySlotsResponse"];
export type EmptySlot = Schemas["EmptySlotItem"];
export type AtRiskSummary = Schemas["AtRiskSummary"];
export type DashboardMarketing = Schemas["DashboardMarketing"];

export const automationNames: Record<AutomationType, string> = {
  Birthday: "Doğum günü",
  WinBack: "Geri kazanma",
  VisitThanks: "Randevu sonrası teşekkür",
  Survey: "Memnuniyet anketi",
  DueReminder: "Vade günü hatırlatması",
};

export const automationDescriptions: Record<AutomationType, string> = {
  Birthday: "Doğum günü olan müşteriye, seçtiğiniz saatte.",
  WinBack: "Uzun süredir gelmeyen müşteriye; aynı kişiye en fazla belirli günde bir.",
  VisitThanks: "Randevu tamamlandıktan sonra teşekkür.",
  Survey: "Randevu tamamlandıktan sonra 1-5 puan linki; düşük puanda yorum size gelir.",
  DueReminder: "Vadesi bugün gelen alacağı olan müşteriye ödeme hatırlatması (bilgilendirme).",
};

/** Adres parçası ↔ tür (/panel/kampanyalar/otomasyonlar/[tur]). */
export const automationSlugs: Record<AutomationType, string> = {
  Birthday: "dogum-gunu",
  WinBack: "geri-kazanma",
  VisitThanks: "tesekkur",
  Survey: "anket",
  DueReminder: "vade",
};

// Personel kartı, mesai ve izin (Sprint 7A)
export type StaffSalary = Schemas["StaffSalaryDto"];
export type SalaryBasis = Schemas["SalaryBasis"];
export type SalaryPayments = Schemas["SalaryPaymentsResponse"];
export type SalaryPayment = Schemas["SalaryPaymentItem"];
export type TimeEntry = Schemas["TimeEntryResponse"];
export type MyClock = Schemas["MyClockResponse"];
export type LeaveType = Schemas["LeaveTypeResponse"];
export type LeaveRequest = Schemas["LeaveRequestResponse"];
export type LeaveRequestList = Schemas["LeaveRequestListResponse"];
export type LeaveStatus = Schemas["LeaveStatus"];
export type HalfDay = Schemas["HalfDay"];
export type LeavePreview = Schemas["LeavePreviewResponse"];
export type LeaveConflict = Schemas["LeaveConflict"];
export type LeaveCalendar = Schemas["LeaveCalendarResponse"];
export type LeaveCalendarItem = Schemas["LeaveCalendarItem"];
export type LeaveBalance = Schemas["LeaveBalanceResponse"];
export type LeaveBalanceEntry = Schemas["LeaveBalanceEntryResponse"];
export type LeaveBalanceKind = Schemas["LeaveBalanceKind"];
export type TimeReport = Schemas["TimeReportResponse"];
export type StaffSettings = Schemas["StaffSettingsDto"];

export const salaryBasisNames: Record<SalaryBasis, string> = { Gross: "Brüt", Net: "Net" };

export const leaveStatusNames: Record<LeaveStatus, string> = {
  Pending: "Bekliyor",
  Approved: "Onaylandı",
  Rejected: "Reddedildi",
  Cancelled: "İptal edildi",
};

export const halfDayNames: Record<HalfDay, string> = {
  None: "Tam gün",
  Morning: "Öğleden önce",
  Afternoon: "Öğleden sonra",
};

export const leaveBalanceKindNames: Record<LeaveBalanceKind, string> = {
  Opening: "Açılış bakiyesi",
  Accrual: "Yıl dönümü hakkı",
  Adjustment: "Düzeltme",
  Usage: "Kullanılan izin",
  UsageReversal: "İptal edilen izin",
};

/** API'deki LeaveTexts.LegalNote ile aynı. */
export const LEGAL_NOTE = "Yasal bilgi amaçlıdır; kesin uygulama için mali müşavirinize danışın.";

// Sprint 7B: prim, raporlar, Bugün'de aktif personel
export type CommissionTarget = Schemas["CommissionTarget"];
export type CommissionType = Schemas["CommissionType"];
export type CommissionKind = Schemas["CommissionKind"];
export type CommissionRule = Schemas["CommissionRuleResponse"];
export type CommissionRuleRequest = Schemas["CommissionRuleRequest"];
export type CommissionMonth = Schemas["CommissionMonthResponse"];
export type CommissionItem = Schemas["CommissionItem"];
export type StaffReport = Schemas["StaffReportResponse"];
export type CampaignReport = Schemas["CampaignReportResponse"];
export type SurveyReport = Schemas["SurveyReportResponse"];
export type DashboardStaff = Schemas["DashboardStaff"];
export type DashboardMyClock = Schemas["DashboardMyClock"];

export const commissionTargetNames: Record<CommissionTarget, string> = {
  All: "Bütün satışlar",
  Service: "Hizmetler",
  Product: "Ürünler",
  Category: "Kategori",
  Item: "Tek ürün/hizmet",
};

// Sprint 8A: SMS'siz pilot (WhatsApp bildirimleri)
export type WhatsAppNoticeKind = Schemas["WhatsAppNoticeKind"];
export type TomorrowNotices = Schemas["TomorrowNotices"];
export type TomorrowNotice = Schemas["TomorrowNotice"];

// Sprint 8A: süper admin (/platform) ve geri bildirim
export type AdminOverview = Schemas["AdminOverview"];
export type AdminBusinessDetail = Schemas["AdminBusinessDetail"];
export type AdminUserItem = Schemas["AdminUserItem"];
export type AdminNote = Schemas["AdminNote"];
export type AdminActivityItem = Schemas["AdminActivityItem"];
export type AdminFeedbackItem = Schemas["AdminFeedbackItem"];
export type FeedbackStatus = Schemas["FeedbackStatus"];
export type PlanStatus = Schemas["PlanStatus"];
export type PlatformAction = Schemas["PlatformAction"];

export const feedbackStatusNames: Record<FeedbackStatus, string> = {
  New: "Yeni",
  InProgress: "İnceleniyor",
  Resolved: "Çözüldü",
  Closed: "Kapandı",
};

export const platformActionNames: Record<PlatformAction, string> = {
  PlanChanged: "Paket değişti",
  PilotChanged: "Pilot değişti",
  ReadOnlyChanged: "Salt okunur değişti",
  TrialExtended: "Deneme uzatıldı",
  TrialEnded: "Deneme bitirildi",
  SmsCreditsAdded: "SMS kredisi yüklendi",
  NoteAdded: "Not eklendi",
  FeedbackUpdated: "Geri bildirim güncellendi",
  PlanSettingsChanged: "Paket ayarları değişti",
};
