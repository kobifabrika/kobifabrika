import { describe, expect, it } from "vitest";

import { auditActionLabels, auditDetail } from "./audit";

describe("aktivite günlüğü metinleri", () => {
  it("her işlemin Türkçe etiketi var", () => {
    expect(Object.values(auditActionLabels).every((l) => l.length > 0)).toBe(true);
  });

  it("ayrıntıları API'nin changes alanından Türkçe üretir", () => {
    expect(auditDetail({ action: "InvitationSent", changes: { email: "a@b.com", role: "Staff" } })).toBe("a@b.com · Personel");
    expect(auditDetail({ action: "RoleChanged", changes: { userId: "x", from: "Staff", to: "Manager" } })).toBe(
      "Personel → Yönetici",
    );
    expect(
      auditDetail({ action: "BusinessUpdated", changes: { name: { from: "A", to: "B" }, district: { from: "x", to: "y" } } }),
    ).toBe("Değişen: ad, ilçe");
    expect(auditDetail({ action: "ModulesChanged", changes: { to: { appointments: true, stock: false } } })).toBe(
      "Randevular açık, Stok kapalı",
    );
    expect(auditDetail({ action: "Login", changes: null })).toBe("");
  });
});
