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

/** Kaynak listesi sabit; sıra arayüzdeki sıradır. */
export const sourceNames: Record<ContactSource, string> = {
  Instagram: "Instagram",
  Google: "Google",
  Referral: "Tavsiye",
  WalkIn: "Yoldan geçerken",
  Facebook: "Facebook",
  TikTok: "TikTok",
  Other: "Diğer",
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
