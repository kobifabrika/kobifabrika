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
};

const fieldLabels: Record<string, string> = {
  name: "ad",
  sector: "sektör",
  phone: "telefon",
  email: "e-posta",
  address: "adres",
  city: "il",
  district: "ilçe",
};

type Changes = Record<string, unknown>;
const role = (value: unknown) => roleNames[value as Role] ?? String(value ?? "");
const text = (value: unknown) => (typeof value === "string" ? value : "");

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
