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
