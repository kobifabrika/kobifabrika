import { describe, expect, it } from "vitest";

import { allNavItems, isActive, navGroups, panelHref, visibleNavGroups, visibleQuickActions } from "./navigation";

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

describe("şablona ve role göre menü", () => {
  const labels = (groups: ReturnType<typeof visibleNavGroups>) => groups.flatMap((g) => g.items.map((i) => i.label));

  it("Hizmet şablonunda Stok, Perakende şablonunda Randevular gizlenir", () => {
    expect(labels(visibleNavGroups("Owner", { appointments: true, stock: false }))).not.toContain("Stok");
    expect(labels(visibleNavGroups("Owner", { appointments: false, stock: true }))).not.toContain("Randevular");
    expect(labels(visibleNavGroups("Owner", { appointments: true, stock: true }))).toHaveLength(12);
  });

  it("Personel Ayarlar ve Raporlar'ı görmez; Mali Müşavir görür", () => {
    const all = { appointments: true, stock: true };
    expect(labels(visibleNavGroups("Staff", all))).not.toContain("Ayarlar");
    expect(labels(visibleNavGroups("Staff", all))).not.toContain("Raporlar");
    expect(labels(visibleNavGroups("Accountant", all))).toContain("Ayarlar");
    expect(labels(visibleNavGroups("Manager", all))).toContain("Ayarlar");
  });

  it("Cari ve Kasa'yı Personel görmez; Müşteriler'i herkes görür", () => {
    const all = { appointments: true, stock: true };
    expect(labels(visibleNavGroups("Staff", all))).not.toContain("Cari ve Kasa");
    expect(labels(visibleNavGroups("Staff", all))).toContain("Müşteriler");
    expect(labels(visibleNavGroups("Accountant", all))).toContain("Cari ve Kasa");
  });

  it("Personel menüsünü Sahip, Yönetici ve Mali Müşavir görür; Personel rolü görmez", () => {
    const all = { appointments: true, stock: true };
    expect(labels(visibleNavGroups("Owner", all))).toContain("Personel");
    expect(labels(visibleNavGroups("Accountant", all))).toContain("Personel");
    expect(labels(visibleNavGroups("Staff", all))).not.toContain("Personel");
  });
});

describe("hızlı işlemler", () => {
  const labels = (role: Parameters<typeof visibleQuickActions>[0], modules = { appointments: true, stock: true }) =>
    visibleQuickActions(role, modules).map((a) => a.label);

  it("Satış, teklif ve randevu herkese (Mali Müşavir hariç) görünür", () => {
    expect(labels("Owner")).toEqual(["Yeni satış", "Yeni teklif", "Yeni randevu", "Yeni müşteri", "Tahsilat", "Gider ekle"]);
    expect(labels("Staff")).toEqual(["Yeni satış", "Yeni teklif", "Yeni randevu", "Yeni müşteri"]);
    expect(labels("Accountant")).toEqual([]);
  });

  it("Yeni randevu takvimde yeni randevu penceresini açar", () => {
    expect(visibleQuickActions("Staff", null).find((a) => a.label === "Yeni randevu")?.href).toBe("/panel/randevular?yeni=1");
  });

  it("Gider ekle telefonda tek dokunuşla gider formunu açar; Personel ve Mali Müşavir görmez", () => {
    expect(visibleQuickActions("Manager", null).find((a) => a.label === "Gider ekle")?.href).toBe("/panel/cari-kasa/gelir-gider/yeni?tur=gider");
    expect(labels("Staff")).not.toContain("Gider ekle");
    expect(labels("Accountant")).not.toContain("Gider ekle");
  });

  it("Randevular modülü kapalıysa randevu gizlenir; satış adresi hızlı satış ekranıdır", () => {
    expect(labels("Owner", { appointments: false, stock: true })).not.toContain("Yeni randevu");
    expect(visibleQuickActions("Owner", null).find((a) => a.label === "Yeni satış")?.href).toBe("/panel/satislar/yeni");
  });
});
