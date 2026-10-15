import type { Page, TestInfo } from "@playwright/test";

import { addMember, createProduct, createSale, line } from "./support/api";
import { createService, createStaff } from "./support/appointments";
import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

async function scan(page: Page, barcode: string) {
  const search = page.getByRole("searchbox", { name: "Ürün ara" });
  await search.click();
  await page.keyboard.type(barcode, { delay: 5 });
  await page.keyboard.press("Enter");
}

async function payCash(page: Page, info: TestInfo) {
  const pay = isDesktop(info) ? page.locator("aside").filter({ hasText: "Satışı tamamla" }) : page.getByRole("dialog");
  if (!isDesktop(info)) await page.getByRole("button", { name: "Ödeme al" }).click();
  await pay.getByRole("radio", { name: "Nakit" }).click();
  await pay.getByRole("button", { name: /Satışı tamamla/ }).click();
  await expect(page.getByText(/SAT-\d{4}-\d{6} kaydedildi/)).toBeVisible();
}

const month = () => new Intl.DateTimeFormat("sv-SE", { timeZone: "Europe/Istanbul" }).format(new Date()).slice(0, 7);

/**
 * Sprint 7B prim: kural ekleme (Personel → Prim kuralları) → hızlı satışta satır personeli seçimi (satırda küçük gri ad) →
 * Raporlar → Prim (personel toplamı, kalem, "Satışı giren") → satış iptali → aynı ayda eksi satır → Excel indirilir.
 * Personel rolü varsayılanı kendisi; "Primim"de sadece kendi primi.
 */
test("prim: kural → satır personeli → rapor → iptalde eksi satır → Excel; Personel'de Primim", async ({ owner, ownerPage: page, visit, check, pageAs, guard }, info) => {
  await createProduct(owner, { name: "Şampuan", salePrice: 240, barcode: "8690000007001", trackStock: false, initialStock: null });
  await createService(owner, "Saç kesimi", 30, 300);
  await createStaff(owner, "Mehmet Kalfa");
  const staffUser = await addMember(owner, "Staff", "Personel Ece");
  const { user } = await staffUser.get<{ user: { id: string } }>("/api/auth/me");
  const ece = await createStaff(owner, "Ece Kalfa", user.id);

  // Kurallar: arayüzden "Herkes · Ürünler · 15 TL/adet"; API'den "Herkes · Hizmetler · %10".
  await visit(page, "/panel/personel?sekme=prim", "Personel · prim kuralları (boş)");
  await expect(page.getByText("Henüz prim kuralı yok")).toBeVisible();
  await page.getByRole("button", { name: "Kural ekle" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Neye").selectOption({ label: "Ürünler" });
  await dialog.getByLabel("Prim türü").selectOption({ label: "Adet başına TL" });
  await dialog.getByLabel("Tutar (TL, adet başına)").fill("15");
  await check(page, "Prim kuralı penceresi");
  await dialog.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Kural eklendi")).toBeVisible();
  await owner.post("/api/commission-rules", { staffMemberId: null, target: "Service", categoryId: null, productId: null, type: "Percent", value: 10, isActive: true });
  await page.reload();
  const rules = page.getByRole("region", { name: "Prim kuralları" });
  await expect(rules.getByText("Herkes · Ürünler")).toBeVisible();
  await expect(rules.getByText("15,00 TL/adet")).toBeVisible();
  await expect(rules.getByText("Herkes · Hizmetler")).toBeVisible();
  await check(page, "Personel · prim kuralları");

  // Hızlı satış (Sahip; personele bağlı değil): satırda personel seçilir, satırda küçük gri ad.
  await visit(page, "/panel/satislar/yeni", "Hızlı satış · prim");
  await scan(page, "8690000007001");
  await expect(toast(page, "Şampuan eklendi")).toBeVisible();
  const cart = page.getByRole("list", { name: "Sepet" });
  await cart.getByRole("button", { name: /Şampuan/ }).first().click();
  const lineDialog = page.getByRole("dialog");
  await expect(lineDialog.getByLabel("Personel")).toHaveValue("");
  await lineDialog.getByLabel("Personel").selectOption({ label: "Mehmet Kalfa" });
  await check(page, "Satır penceresi · personel");
  await lineDialog.getByRole("button", { name: "Tamam" }).click();
  await expect(cart.getByText("Mehmet Kalfa")).toBeVisible();
  await payCash(page, info);
  const sale = (await owner.get<{ items: { id: string; no: string }[] }>("/api/sales")).items[0];

  // Raporlar → Prim: Mehmet 15,00 TL, "Satışı giren" Sahip.
  await visit(page, `/panel/raporlar?rapor=prim`, "Rapor · prim");
  const report = page.getByRole("region", { name: "Prim raporu" });
  await expect(report.getByText("Mehmet Kalfa").filter({ visible: true }).first()).toBeVisible();
  await expect(report.getByText("15,00 TL").filter({ visible: true }).first()).toBeVisible();
  await expect(report.getByText(sale.no).filter({ visible: true }).first()).toBeVisible();
  await expect(report.getByText(/Deneme Sahip/).filter({ visible: true }).first()).toBeVisible();

  // İptal: aynı ayda eksi satır, net 0.
  await visit(page, `/panel/satislar/${sale.id}`, "Satış detayı · satır personeli");
  await expect(page.getByText("Personel: Mehmet Kalfa")).toBeVisible();
  await page.getByRole("button", { name: "Satışı iptal et" }).click();
  await page.getByRole("dialog").getByRole("button", { name: /iptal et/i }).click();
  await expect(toast(page, `${sale.no} iptal edildi.`)).toBeVisible();
  await visit(page, `/panel/raporlar?rapor=prim`, "Rapor · prim (iptal)");
  await expect(report.getByText("İptal").filter({ visible: true }).first()).toBeVisible();
  await expect(report.getByText("-15,00 TL").filter({ visible: true }).first()).toBeVisible();
  const commissions = await owner.get<{ total: number; items: { kind: string }[] }>(`/api/reports/commissions?month=${month()}`);
  expect(commissions.total).toBe(0);
  expect(commissions.items.map((i) => i.kind).sort()).toEqual(["Accrual", "Reversal"]);

  // Excel indirilir (WebKit indirme isteğini "Frame load interrupted" ile kapatır; indirme yine olur).
  guard.allow("network", /\/api\/reports\/commissions\.xlsx/);
  const download = page.waitForEvent("download");
  await page.getByRole("link", { name: "Excel'e aktar" }).click();
  expect((await download).suggestedFilename()).toMatch(/^rapor-prim-\d{4}-\d{2}-01_\d{4}-\d{2}-\d{2}\.xlsx$/);

  // Personel: hızlı satışta varsayılan kendisi; Primim'de sadece kendi primi.
  const staffPage = await pageAs(staffUser);
  await visit(staffPage, "/panel/satislar/yeni", "Hızlı satış · Personel");
  await scan(staffPage, "8690000007001");
  await expect(staffPage.getByRole("list", { name: "Sepet" }).getByText("Ece Kalfa")).toBeVisible();
  await payCash(staffPage, info);
  await createSale(owner, [line(null, 1, { description: "Masaj", unitPrice: 600, staffMemberId: ece.id })], [{ method: "Cash", amount: 600 }]);
  await visit(staffPage, "/panel/mesai-izin?sekme=prim", "Primim");
  const mine = staffPage.getByRole("region", { name: "Primim" });
  await expect(mine.getByText("15,00 TL", { exact: true }).first()).toBeVisible();
  await expect(mine.getByText("Mehmet Kalfa")).toHaveCount(0);
  await expect(mine.getByText("1 kalem")).toBeVisible();
});
