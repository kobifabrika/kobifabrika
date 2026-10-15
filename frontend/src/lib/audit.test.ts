import { describe, expect, it } from "vitest";

import { auditActionLabels, auditDetail } from "./audit";

describe("aktivite günlüğü metinleri", () => {
  it("her işlemin Türkçe etiketi var", () => {
    expect(Object.values(auditActionLabels).every((l) => l.length > 0)).toBe(true);
  });

  it("ayrıntıları API'nin changes alanından Türkçe üretir", () => {
    expect(auditDetail({ action: "InvitationSent", changes: { email: "a@b.com", role: "Staff" } })).toBe(
      "a@b.com · Personel",
    );
    expect(auditDetail({ action: "RoleChanged", changes: { userId: "x", from: "Staff", to: "Manager" } })).toBe(
      "Personel → Yönetici",
    );
    expect(
      auditDetail({
        action: "BusinessUpdated",
        changes: { name: { from: "A", to: "B" }, district: { from: "x", to: "y" } },
      }),
    ).toBe("Değişen: ad, ilçe");
    expect(auditDetail({ action: "ModulesChanged", changes: { to: { appointments: true, stock: false } } })).toBe(
      "Randevular açık, Stok kapalı",
    );
    expect(
      auditDetail({
        action: "QuoteExtended",
        changes: { no: "TKL-2026-0001", from: "2026-10-01", validUntil: "2026-10-26" },
      }),
    ).toBe("TKL-2026-0001 · 01.10.2026 → 26.10.2026");
    expect(
      auditDetail({
        action: "TimeEntryCorrected",
        changes: {
          clockIn: { from: "2026-10-14T06:00:00Z", to: "2026-10-14T06:30:00Z" },
          clockOut: { from: "2026-10-14T15:00:00Z", to: "2026-10-14T15:00:00Z" },
        },
      }),
    ).toBe("Giriş 14 Ekim 2026 09:00 → 14 Ekim 2026 09:30");
    expect(
      auditDetail({ action: "LeaveApproved", changes: { staff: "Ayşe Usta", type: "Yıllık izin", start: "2026-11-03", end: "2026-11-05" } }),
    ).toBe("Ayşe Usta · Yıllık izin · 03.11.2026 – 05.11.2026");
    expect(auditDetail({ action: "Login", changes: null })).toBe("");
  });

  it("prim kuralı ve satır personeli değişikliklerini yazar (7B)", () => {
    expect(auditDetail({ action: "CommissionRuleChanged", changes: { created: "Herkes · Hizmetler · %10" } })).toBe(
      "Eklendi: Herkes · Hizmetler · %10",
    );
    expect(
      auditDetail({
        action: "CommissionRuleChanged",
        changes: { before: { text: "Herkes · Hizmetler · %10", isActive: true }, after: { text: "Herkes · Hizmetler · %12", isActive: false } },
      }),
    ).toBe("Herkes · Hizmetler · %10 → Herkes · Hizmetler · %12 (pasif)");
    expect(
      auditDetail({
        action: "SaleLineStaffChanged",
        changes: { no: "SAT-2026-000012", line: "Saç kesimi", staff: { from: "Ayşe Usta", to: "Mehmet Kalfa" } },
      }),
    ).toBe("SAT-2026-000012 · Saç kesimi · Ayşe Usta → Mehmet Kalfa");
    expect(auditDetail({ action: "ReportExported", changes: { report: "prim" } })).toBe("Prim");
  });
});
