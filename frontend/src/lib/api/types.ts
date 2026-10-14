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
