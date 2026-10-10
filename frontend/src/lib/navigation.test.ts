import { describe, expect, it } from "vitest";

import { allNavItems, isActive, navGroups, panelHref } from "./navigation";

describe("panel menüsü", () => {
  it("Faz 1 menüsündeki 12 ekranı iki grupta içerir", () => {
    expect(navGroups.map((g) => g.title)).toEqual(["İşletme", "Yönetim"]);
    expect(allNavItems.map((i) => i.label)).toEqual([
      "Bugün",
      "Randevular",
      "Satışlar",
      "Müşteriler",
      "Ürünler/Hizmetler",
      "Stok",
      "Cari ve Kasa",
      "Kampanyalar",
      "Personel",
      "Mesai ve İzin",
      "Raporlar",
      "Ayarlar",
    ]);
  });

  it("URL'ler Türkçe ve ASCII", () => {
    for (const item of allNavItems) {
      expect(item.slug).toMatch(/^[a-z-]*$/);
    }
  });

  it("aktif menüyü doğru bulur", () => {
    expect(panelHref("")).toBe("/panel");
    expect(isActive("/panel", "")).toBe(true);
    expect(isActive("/panel/musteriler", "")).toBe(false);
    expect(isActive("/panel/musteriler/123", "musteriler")).toBe(true);
  });
});
