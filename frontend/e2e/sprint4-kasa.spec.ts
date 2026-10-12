import { createContact } from "./support/api";
import { accounts } from "./support/finance";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 4 kasa ve banka: hesap ekleme (açılış bakiyesi), transfer (eksi bakiye uyarısı), hesap kartı (tarih filtresi,
 * giriş/çıkış toplamı), ters kayıt (bağlı görünür, toplamdan düşer), pasife alma engeli, tedarikçiye ödeme, Excel.
 */
test("kasa ve banka: hesap, transfer, hesap kartı, ters kayıt, ödeme ve Excel", async ({ owner, ownerPage: page, visit, check, guard }) => {
  await visit(page, "/panel/cari-kasa?sekme=kasa", "Kasa ve banka");

  // Hesap ekle: banka, açılış 1.000 TL
  await page.getByRole("button", { name: "Hesap ekle" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Hesap adı").fill("Ziraat");
  await dialog.getByLabel("IBAN (isteğe bağlı)").fill("TR40 0006 2000 0000 0000 0000 01");
  await dialog.getByLabel("Açılış bakiyesi (TL, isteğe bağlı)").fill("1.000");
  await check(page, "Hesap ekle");
  await dialog.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Hesap eklendi.")).toBeVisible();
  const ziraat = page.getByRole("link", { name: /Ziraat/ });
  await expect(ziraat).toContainText("1.000,00 TL");

  // Transfer: Nakit Kasa (0) → Ziraat 200; eksi bakiye uyarısı (engellemez)
  await page.getByRole("button", { name: "Transfer" }).click();
  const transfer = page.getByRole("dialog");
  const ids = await accounts(owner);
  await transfer.getByLabel("Nereden").selectOption(ids["Nakit Kasa"].id);
  await transfer.getByLabel("Nereye").selectOption(ids["Ziraat"].id);
  await transfer.getByLabel("Tutar (TL)").fill("200");
  await expect(transfer.getByText(/Nakit Kasa bakiyesi -200,00 TL olacak/)).toBeVisible();
  await check(page, "Transfer");
  await transfer.getByRole("button", { name: "Transferi kaydet" }).click();
  await expect(toast(page, "Transfer kaydedildi.")).toBeVisible();
  await expect(page.getByRole("link", { name: /Nakit Kasa/ })).toContainText("Eksi bakiye");
  await expect(ziraat).toContainText("1.200,00 TL");

  // Hesap kartı
  await ziraat.click();
  await expect(page.getByRole("heading", { name: "Ziraat" })).toBeVisible();
  await check(page, "Hesap kartı");
  await expect(page.getByText("TR40 0006 2000 0000 0000 0000 01")).toBeVisible();
  const totals = page.getByRole("region", { name: "Hareketler" }).locator("dl");
  await expect(totals).toContainText("Giriş");
  await expect(totals).toContainText("1.200,00 TL");

  // Transferin ters kaydı: iki taraf da geri döner; satırlar bağlı görünür, giriş toplamından düşülür.
  await page.getByRole("button", { name: "Ters kayıt" }).first().click();
  await expect(page.getByRole("dialog")).toContainText("Transferin Nakit Kasa tarafı da geri alınır.");
  await check(page, "Ters kayıt");
  await page.getByRole("dialog").getByRole("button", { name: "Ters kaydı yap" }).click();
  await expect(toast(page, "Ters kayıt yapıldı.")).toBeVisible();
  await expect(page.getByText("Ters kaydı yapıldı").filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText(/Ters kayıt · .* tarihli harekete/).filter({ visible: true }).first()).toBeVisible();
  await expect(totals).toContainText("1.000,00 TL");

  // Tarih filtresi: geçen ayda hareket yok.
  await page.getByRole("button", { name: "Geçen ay" }).click();
  await expect(page.getByText("Bu aralıkta hareket yok.")).toBeVisible();
  await page.getByRole("button", { name: "Bu ay" }).click();

  // Bakiyesi olan hesap pasife alınamaz.
  guard.allow(409, /\/deactivate$/);
  page.once("dialog", (d) => d.accept());
  await page.getByRole("button", { name: "Pasife al" }).click();
  await expect(toast(page, /bakiyesi 1\.000,00 TL/)).toBeVisible();

  // Excel (hesap hareketleri)
  const href = await page.getByRole("link", { name: "Excel'e aktar" }).getAttribute("href");
  const file = await page.request.get(href!);
  expect(file.status()).toBe(200);
  expect(file.headers()["content-type"]).toContain("spreadsheetml");

  // Tedarikçiye ödeme: kasa ekranından, tedarikçi seçerek
  await createContact(owner, { firstName: "Demir", lastName: "Toptan", isCustomer: false, isSupplier: true });
  await page.goto("/panel/cari-kasa?sekme=kasa");
  await page.getByRole("button", { name: "Ödeme yap" }).click();
  const pay = page.getByRole("dialog");
  await pay.getByPlaceholder("Ad veya telefon ara").fill("Demir");
  await pay.getByRole("button", { name: /Demir Toptan/ }).click();
  await pay.getByLabel("Ödenen tutar (TL)").fill("150");
  await check(page, "Ödeme yap");
  await pay.getByRole("button", { name: "Ödemeyi kaydet" }).click();
  await expect(toast(page, /Ödeme kaydedildi/)).toBeVisible();
});
