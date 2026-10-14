import type { Page } from "@playwright/test";

import { ownerWithBusiness } from "./support/api";
import { logout } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

test.use({ ownerSector: "RetailStore" });

/** Alt çubuktaki düğmelerin kutuları: eşit genişlik, (+) tam ortada. */
async function bottomButtons(page: Page) {
  const nav = page.getByRole("navigation", { name: "Alt menü" });
  const items = nav.locator(":scope > div > a, :scope > div > button");
  const labels = await items.evaluateAll((els) => els.map((el) => el.getAttribute("aria-label") ?? el.textContent?.trim() ?? ""));
  const boxes = await items.evaluateAll((els) => els.map((el) => el.getBoundingClientRect()).map((r) => ({ x: r.x, width: r.width })));
  return { labels, boxes, viewport: page.viewportSize()!.width };
}

/**
 * Sprint 6A mobil kabuk: alt çubukta simetrik 5 düğme (Bugün, Randevu, +, Satış, Müşteri; Perakende'de Randevu yerine Ürün),
 * Menü sağ üstte (kullanıcı menüsüyle birleşik). Çip ve sekme satırlarında kaydırma çubuğu görünmez, son öğe kaydırınca tam görünür.
 */
test("mobil kabuk: 5 düğmeli alt çubuk, sağ üstte Menü, çip satırı", async ({ owner, ownerPage: page, pageAs, visit, check }, info) => {
  test.skip(isDesktop(info), "Telefon düzeni");

  await visit(page, "/panel", "Bugün · yeni alt çubuk");
  const { labels, boxes, viewport } = await bottomButtons(page);
  // Perakende şablonu: Randevular kapalı, yerinde Ürün.
  expect(labels).toEqual(["Bugün", "Ürün", "Hızlı işlem", "Satış", "Müşteri"]);
  const widths = boxes.map((b) => Math.round(b.width));
  expect(Math.max(...widths) - Math.min(...widths)).toBeLessThanOrEqual(1);
  const center = boxes[2]!.x + boxes[2]!.width / 2;
  expect(Math.abs(center - viewport / 2)).toBeLessThanOrEqual(2);

  // Menü sağ üstte: kullanıcı bölümü + ana menü.
  const menu = page.getByRole("banner").getByRole("button", { name: "Menü" });
  const menuBox = (await menu.boundingBox())!;
  expect(menuBox.x + menuBox.width).toBeGreaterThan(viewport - 24);
  await menu.click();
  const sheet = page.getByRole("dialog");
  await expect(sheet.getByRole("link", { name: "Hesabım" })).toBeVisible();
  await expect(sheet.getByRole("navigation", { name: "Ana menü" })).toBeVisible();
  await expect(sheet.getByRole("button", { name: "Çıkış yap" })).toBeVisible();
  await check(page, "Menü · sağ üst");
  await page.keyboard.press("Escape");

  // Çip satırı (Raporlar dönem seçicisi): çubuk gizli, kaydırınca son öğe tam görünür.
  await visit(page, "/panel/raporlar", "Raporlar · çip satırı");
  const row = page.getByRole("group").filter({ has: page.getByRole("button", { name: "Bu yıl" }) }).first();
  expect(await row.evaluate((el) => getComputedStyle(el).scrollbarWidth)).toBe("none");
  await row.evaluate((el) => el.scrollTo({ left: el.scrollWidth }));
  const last = row.getByRole("button").last();
  const lastBox = (await last.boundingBox())!;
  expect(lastBox.x + lastBox.width).toBeLessThanOrEqual(viewport - 15);
  await check(page, "Raporlar · çip satırı sona kaydırıldı");

  // Hizmet şablonu: Randevu düğmesi.
  const salon = await ownerWithBusiness({ sector: "HairBarber" });
  const salonPage = await pageAs(salon);
  await visit(salonPage, "/panel", "Bugün · Hizmet şablonu alt çubuk");
  expect((await bottomButtons(salonPage)).labels).toEqual(["Bugün", "Randevu", "Hızlı işlem", "Satış", "Müşteri"]);

  // Çıkış telefonda Menü'den.
  await logout(page);
  expect(owner.email).toContain("@");
});
