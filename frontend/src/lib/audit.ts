import { roleNames, type AuditAction, type AuditLogItem, type Role } from "@/lib/api/types";

export const auditActionLabels: Record<AuditAction, string> = {
  Login: "Giriş yaptı",
  InvitationSent: "Davet gönderdi",
  InvitationResent: "Daveti yeniden gönderdi",
  InvitationRevoked: "Daveti iptal etti",
  InvitationAccepted: "Daveti kabul etti",
  RoleChanged: "Rol değiştirdi",
  MemberRemoved: "Ekipten çıkardı",
  BusinessUpdated: "İşletme bilgilerini güncelledi",
  SlugChanged: "Randevu linkini değiştirdi",
  WorkingHoursUpdated: "Çalışma saatlerini güncelledi",
  LogoChanged: "Logoyu değiştirdi",
  ModulesChanged: "Modülleri değiştirdi",
  PaymentInfoChanged: "Ödeme bilgilerini (IBAN) değiştirdi",
  ContactCreated: "Müşteri ekledi",
  ContactUpdated: "Müşteri kartını güncelledi",
  ContactArchived: "Müşteriyi arşivledi",
  ContactRestored: "Müşteriyi arşivden çıkardı",
  ContactDeleted: "Müşteriyi sildi",
  ContactsImported: "Excel'den müşteri aktardı",
  ContactsExported: "Müşteri listesini Excel'e aktardı",
  LedgerEntryCreated: "Cari hareket girdi",
  LedgerEntryDeleted: "Cari hareketi sildi",
  OpeningBalanceSet: "Açılış bakiyesi girdi",
  CollectionRecorded: "Tahsilat aldı",
  CollectionReverted: "Tahsilatı geri aldı",
  PaymentRequestCreated: "Tahsilat linki oluşturdu",
  PaymentRequestConfirmed: "Tahsilat linkini onayladı",
  PaymentRequestRejected: '"Ödeme gelmedi" dedi',
  PaymentRequestCancelled: "Tahsilat linkini iptal etti",
  PaymentRequestRenewed: "Tahsilat linkini yeniledi",
  StatementShared: "Ekstre paylaştı",
  ProductCreated: "Ürün/hizmet ekledi",
  ProductUpdated: "Ürünü güncelledi",
  ProductDeleted: "Ürünü sildi",
  ProductsImported: "Excel'den ürün aktardı",
  ProductsExported: "Ürün listesini Excel'e aktardı",
  StockMovementAdded: "Stok hareketi girdi",
  SaleDiscounted: "İndirimli satış yaptı",
  SaleCancelled: "Satışı iptal etti",
  SalesExported: "Satışları Excel'e aktardı",
  QuoteCreated: "Teklif hazırladı",
  QuoteUpdated: "Teklifi düzenledi",
  QuoteShared: "Teklif linki paylaştı",
  QuoteRenewed: "Teklif linkini yeniledi",
  QuoteExtended: "Teklifin geçerliliğini uzattı",
  QuoteAccepted: "Teklif onaylandı",
  QuoteRejected: "Teklif reddedildi",
  QuoteConverted: "Teklifi satışa dönüştürdü",
  QuoteDeleted: "Teklifi sildi",
};

const fieldLabels: Record<string, string> = {
  name: "ad",
  sector: "sektör",
  phone: "telefon",
  email: "e-posta",
  address: "adres",
  city: "il",
  district: "ilçe",
  firstName: "ad",
  lastName: "soyad",
  companyName: "firma",
  birthDate: "doğum tarihi",
  source: "kaynak",
  isCustomer: "müşteri",
  isSupplier: "tedarikçi",
  taxNumber: "vergi no",
  taxOffice: "vergi dairesi",
};

const money = (value: unknown) =>
  typeof value === "number"
    ? `${value.toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} TL`
    : "";

type Changes = Record<string, unknown>;
const role = (value: unknown) => roleNames[value as Role] ?? String(value ?? "");
const text = (value: unknown) => (typeof value === "string" ? value : "");
/** "2026-10-26" → "26.10.2026" */
const day = (value: unknown) => (typeof value === "string" ? value.split("-").reverse().join(".") : "");

/** Günlük satırının kısa Türkçe ayrıntısı (changes JSON'undan). */
export function auditDetail(item: Pick<AuditLogItem, "action" | "changes">): string {
  const c = (item.changes ?? {}) as Changes;
  switch (item.action) {
    case "InvitationSent":
      return `${text(c.email)} · ${role(c.role)}`;
    case "InvitationResent":
    case "InvitationRevoked":
      return text(c.email);
    case "InvitationAccepted":
      return `${text(c.email)} · ${role(c.role)}`;
    case "RoleChanged":
      return `${role(c.from)} → ${role(c.to)}`;
    case "MemberRemoved":
      return role(c.role);
    case "ContactCreated":
    case "ContactArchived":
    case "ContactRestored":
    case "ContactDeleted":
      return text(c.name);
    case "ContactsImported":
      return `${text(c.file)} · ${c.added ?? 0} eklendi, ${c.duplicates ?? 0} tekrar, ${c.errors ?? 0} hatalı`;
    case "ContactsExported":
      return `${c.count ?? 0} kayıt`;
    case "LedgerEntryCreated":
      return `${c.kind === "Debit" ? "Borç" : "Alacak"} ${money(c.amount)} · ${text(c.description)}`;
    case "CollectionRecorded":
      return `${money(c.amount)} · ${text(c.cashAccount)}`;
    case "PaymentRequestCreated":
    case "PaymentRequestConfirmed":
    case "PaymentRequestRenewed":
      return money(c.amount);
    case "ProductCreated":
    case "ProductDeleted":
      return text(c.name) + (c.deactivated ? " · pasife alındı" : "");
    case "ProductUpdated":
      return typeof c.isActive === "boolean" ? `${text(c.name)} · ${c.isActive ? "satışa açıldı" : "pasife alındı"}` : "";
    case "ProductsImported":
      return `${text(c.file)} · ${c.added ?? 0} eklendi, ${c.duplicates ?? 0} tekrar, ${c.errors ?? 0} hatalı`;
    case "ProductsExported":
    case "SalesExported":
      return `${c.count ?? 0} kayıt`;
    case "StockMovementAdded":
      return `${text(c.name)} · ${c.quantity ?? ""}`;
    case "SaleDiscounted":
      return `${text(c.no)} · indirim ${money(c.discount)}`;
    case "SaleCancelled":
      return `${text(c.no)} · ${money(c.total)}${c.reason ? ` · ${text(c.reason)}` : ""}`;
    case "QuoteCreated":
    case "QuoteUpdated":
      return `${text(c.no)} · ${money(c.total)}`;
    case "QuoteExtended":
      return `${text(c.no)} · ${day(c.from)} → ${day(c.validUntil)}`;
    case "QuoteShared":
    case "QuoteRenewed":
    case "QuoteDeleted":
      return text(c.no);
    case "QuoteAccepted":
    case "QuoteRejected":
      return `${text(c.no)} · ${c.via === "Link" ? "müşteri linkten" : "panelden"}${c.note ? ` · ${text(c.note)}` : ""}`;
    case "QuoteConverted":
      return `${text(c.no)} → ${text(c.sale)}`;
    case "StatementShared":
      return c.channel === "Email" ? "E-posta" : c.channel === "WhatsApp" ? "WhatsApp" : "Link";
    case "ContactUpdated":
    case "BusinessUpdated": {
      const fields = Object.keys(c).map((f) => fieldLabels[f] ?? f);
      return fields.length ? `Değişen: ${fields.join(", ")}` : "";
    }
    case "SlugChanged":
      return `${text(c.from)} → ${text(c.to)}`;
    case "LogoChanged":
      return c.removed ? "Logo kaldırıldı" : "";
    case "ModulesChanged": {
      const to = (c.to ?? {}) as { appointments?: boolean; stock?: boolean };
      return `Randevular ${to.appointments ? "açık" : "kapalı"}, Stok ${to.stock ? "açık" : "kapalı"}`;
    }
    default:
      return "";
  }
}
