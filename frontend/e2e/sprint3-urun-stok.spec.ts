import path from "node:path";

import type { Page } from "@playwright/test";

import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

const fixtures = path.join(__dirname, "fixtures");

/** Ürün kartındaki bilgi satırı ("Satış fiyatı (KDV dahil)" gibi). */
const infoRow = (page: Page, label: string) => page.locator("dl > div").filter({ hasText: label }).first();

async function stockMove(page: Page, kind: "Stok girişi" | "Çıkış" | "Sayım", quantity: string) {
  await page.getByRole("button", { name: kind, exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel(kind === "Sayım" ? "Sayılan miktar" : "Miktar", { exact: true }).fill(quantity);
  await dialog.getByRole("button", { name: "Kaydet" }).click();
  await expect(dialog).toHaveCount(0);
}

/** Sprint 3: ürün ekle (KDV dahil/hariç), stok girişi/çıkışı/sayımı, kritik stok bildirimi. */
test("ürün ekleme, KDV dahil/hariç fiyat, stok hareketleri ve kritik stok", async ({
  owner,
  ownerPage: page,
  visit,
  check,
}, info) => {
  // KDV dahil fiyatla ürün
  await visit(page, "/panel/urunler/yeni", "Yeni ürün");
  await page.getByLabel("Ad", { exact: true }).fill("Pamuklu tişört");
  await page.getByRole("radiogroup", { name: "KDV oranı" }).getByRole("radio", { name: "%20" }).click();
  await page.getByLabel("Satış fiyatı (TL)").fill("240");
  await page.getByLabel("Alış fiyatı (TL)").fill("120");
  await page.getByLabel("Barkod").fill("8690000000011");
  await page.getByLabel("Mevcut stok").fill("10");
  await page.getByLabel("Kritik stok seviyesi").fill("3");
  await check(page, "Yeni ürün · dolu form");
  await page.getByRole("button", { name: "Ürünü ekle" }).click();
  await expect(toast(page, "Pamuklu tişört eklendi.")).toBeVisible();
  await expect(page).toHaveURL(/\/panel\/urunler\/[0-9a-f-]{36}$/);
  await expect(infoRow(page, "Satış fiyatı (KDV dahil)")).toContainText("240,00 TL");
  await expect(infoRow(page, "KDV hariç")).toContainText("200,00 TL");
  await check(page, "Ürün kartı");
  const productUrl = page.url();

  // KDV hariç fiyatla ürün: 100 + %20 = 120 KDV dahil
  await page.goto("/panel/urunler/yeni");
  await page.getByLabel("Ad", { exact: true }).fill("Defter");
  await page.getByRole("radiogroup", { name: "KDV oranı" }).getByRole("radio", { name: "%20" }).click();
  await page.getByRole("switch", { name: "Fiyatlar KDV dahil" }).click();
  await page.getByLabel("Satış fiyatı (TL)").fill("100");
  await page.getByRole("button", { name: "Ürünü ekle" }).click();
  await expect(page).toHaveURL(/\/panel\/urunler\/[0-9a-f-]{36}$/);
  await expect(infoRow(page, "Satış fiyatı (KDV dahil)")).toContainText("120,00 TL");
  await expect(infoRow(page, "KDV hariç")).toContainText("100,00 TL");

  // Stok: 10 → giriş 5 → 15 → çıkış 2 → 13 → sayım 2 (kritik 3'ün altı)
  await page.goto(productUrl);
  await stockMove(page, "Stok girişi", "5");
  await expect(toast(page, "Pamuklu tişört: stok 15 adet")).toBeVisible();
  await stockMove(page, "Çıkış", "2");
  await expect(toast(page, "Pamuklu tişört: stok 13 adet")).toBeVisible();
  await stockMove(page, "Sayım", "2");
  await expect(toast(page, "Pamuklu tişört: stok 2 adet")).toBeVisible();
  await expect(page.getByText("Stok kritik seviyede. Sipariş vermeyi unutmayın.")).toBeVisible();

  // Kritik stok: bildirim ve Stok ekranı
  const notifications = await owner.get<{ items: { type: string; title: string }[] }>("/api/notifications");
  expect(notifications.items.some((n) => n.type === "stock.low")).toBe(true);
  await visit(page, "/panel/stok?filtre=kritik", "Stok · kritik");
  await expect(page.getByText("Pamuklu tişört").filter({ visible: true }).first()).toBeVisible();
  await page.reload();
  const bell = page.getByRole("button", { name: /Bildirimler, \d+ okunmamış/ });
  await bell.click();
  await expect(page.getByText(/Pamuklu tişört/).filter({ visible: true }).first()).toBeVisible();
  // Pencere telefonda kenarlardan eşit boşluklu, masaüstünde zilin altında sağa hizalı (2026-10-21).
  const box = (await page.locator("[data-radix-popper-content-wrapper]").boundingBox())!;
  const viewport = page.viewportSize()!;
  if (isDesktop(info)) {
    const bellBox = (await bell.boundingBox())!;
    expect(Math.abs(box.x + box.width - (bellBox.x + bellBox.width))).toBeLessThan(2);
    expect(box.y).toBeGreaterThan(bellBox.y + bellBox.height - 1);
  } else {
    expect(Math.abs(box.x - (viewport.width - box.x - box.width))).toBeLessThan(2);
    expect(box.x).toBeGreaterThanOrEqual(15);
  }
});

test("Excel'den ürün içe aktarma: tekrar eden barkod atlanır", async ({ owner, ownerPage: page, visit, check }) => {
  await visit(page, "/panel/urunler/ice-aktar", "Ürün içe aktar");
  await page.locator('input[type="file"]').setInputFiles(path.join(fixtures, "urunler.xlsx"));
  await expect(page.getByText("· 4 satır")).toBeVisible();
  await page.getByRole("button", { name: "Önizle" }).click();
  await expect(page.getByRole("button", { name: "3 kaydı aktar" })).toBeVisible();
  await check(page, "Ürün içe aktar · önizleme");
  await page.getByRole("button", { name: "3 kaydı aktar" }).click();
  await expect(page.getByText("3 kayıt eklendi.")).toBeVisible();
  await expect(page.getByText(/1 satır .* nedeniyle atlandı/)).toBeVisible();

  const products = await owner.get<{ items: { name: string; stockQuantity: number | null }[] }>("/api/products?pageSize=50");
  expect(products.items.map((p) => p.name).sort()).toEqual(["Filtre kahve 250 g", "Kahve demleme kursu", "Kupa bardak"]);
  expect(products.items.find((p) => p.name === "Filtre kahve 250 g")?.stockQuantity).toBe(20);
  await visit(page, "/panel/urunler", "Ürünler · içe aktarma sonrası");
});
