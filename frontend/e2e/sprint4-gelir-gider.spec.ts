import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

/**
 * Sprint 4 gelir-gider: hızlı işlemden "Gider ekle" (telefonda +), eksi bakiye uyarısı, gelir, liste toplamı ve ters kayıt,
 * Ayarlar → Finans (kategori ekle/pasife al, vade günü).
 */
test("gider ve gelir: hızlı işlem, liste, ters kayıt ve kategoriler", async ({ ownerPage: page, visit, check }, info) => {
  await visit(page, "/panel", "Bugün");
  await page.getByRole("button", { name: "Hızlı işlem" }).filter({ visible: true }).click();
  if (isDesktop(info)) await page.getByRole("menuitem", { name: "Gider ekle" }).click();
  else await page.getByRole("dialog").getByRole("link", { name: "Gider ekle" }).click();
  await expect(page).toHaveURL(/\/panel\/cari-kasa\/gelir-gider\/yeni\?tur=gider/);
  await expect(page.getByRole("heading", { name: "Gider ekle" })).toBeVisible();

  await page.getByLabel("Tutar (TL)").fill("850");
  await page.getByText("Kira", { exact: true }).click();
  await expect(page.getByText(/Nakit Kasa bakiyesi -850\sTL olacak/)).toBeVisible();
  await page.getByLabel("Açıklama (isteğe bağlı)").fill("Ekim kirası");
  await check(page, "Gider ekle");
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(page.getByText("850 TL gider kaydedildi.")).toBeVisible();
  await expect(page.getByText("Nakit Kasa bakiyesi: -850 TL (eksi bakiye)")).toBeVisible();
  await check(page, "Gider kaydedildi");

  // Gelir
  await page.goto("/panel/cari-kasa/gelir-gider/yeni?tur=gelir");
  await page.getByLabel("Tutar (TL)").fill("100");
  await page.getByText("Diğer gelir", { exact: true }).click();
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(page.getByText("100 TL gelir kaydedildi.")).toBeVisible();

  // Liste: giderler toplamı; ters kayıt toplamdan düşer.
  await visit(page, "/panel/cari-kasa?sekme=gelir-gider", "Gelir-gider listesi");
  await expect(page.getByText(/Gider toplamı · 1 kayıt/)).toBeVisible();
  await page.getByRole("button", { name: "Ters kayıt" }).first().click();
  await page.getByRole("dialog").getByLabel("Neden (isteğe bağlı)").fill("Yanlış tutar");
  await page.getByRole("dialog").getByRole("button", { name: "Ters kaydı yap" }).click();
  await expect(toast(page, "Ters kayıt yapıldı.")).toBeVisible();
  await expect(page.getByText(/Gider toplamı · 2 kayıt/)).toBeVisible();
  await expect(page.getByText("Ters kaydı yapıldı")).toBeVisible();
  await expect(page.getByText(/Gider toplamı/).locator("..")).toContainText(/(?<![\d.])0\sTL/);

  // Ayarlar → Finans: kategori ekle, pasife al; vade günü
  await visit(page, "/panel/ayarlar/finans", "Finans ayarları");
  await page.getByRole("button", { name: "Gider kategorisi ekle" }).click();
  await page.getByLabel("Yeni kategori adı").fill("Reklam");
  await page.getByRole("button", { name: "Kategoriyi kaydet" }).click();
  await expect(toast(page, "Kategori eklendi.")).toBeVisible();
  const row = page.getByRole("listitem").filter({ hasText: "Reklam" });
  await row.getByRole("button", { name: "Pasife al" }).click();
  await expect(row).toContainText("Pasif");
  await page.getByLabel("Varsayılan vade (gün)").fill("15");
  await page.getByRole("button", { name: "Vadeyi kaydet" }).click();
  await expect(toast(page, "Finans ayarları kaydedildi.")).toBeVisible();
  await check(page, "Finans ayarları · kaydedildi");
});
