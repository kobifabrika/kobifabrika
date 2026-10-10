import type { Role } from "@/lib/api/types";

/**
 * Arayüzdeki görünürlük matrisi. Gerçek yetki kontrolü backend policy'lerinde
 * (backend/src/KobiFabrika.Api/Common/Authorization/Policies.cs); burası sadece menü ve butonları gizler.
 */
const matrix = {
  /** Ayarlar sayfası (Accountant salt okunur). */
  viewSettings: ["Owner", "Manager", "Accountant"],
  manageBusiness: ["Owner", "Manager"],
  manageTeam: ["Owner", "Manager"],
  viewAuditLog: ["Owner", "Manager", "Accountant"],
  /** Finans raporları: Staff göremez (docs/01-faz1-kapsam.md, A). */
  viewReports: ["Owner", "Manager", "Accountant"],
} as const satisfies Record<string, readonly Role[]>;

export type Permission = keyof typeof matrix;

export function can(role: Role | null | undefined, permission: Permission): boolean {
  return !!role && (matrix[permission] as readonly Role[]).includes(role);
}
