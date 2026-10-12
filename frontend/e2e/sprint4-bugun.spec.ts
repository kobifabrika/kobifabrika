import { addMember, createContact, createProduct, createSale, line } from "./support/api";
import { addDebit } from "./support/finance";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 4 Bugün ekranı: satış/tahsilat/alacak/kritik stok kartları, 30 gün grafiği, bakiyeler, son satışlar;
 * "Tahsilat al" müşteri seçicili pencere açar; Personel kendi satışını görür, finans kartlarını görmez; Mali Müşavir salt okunur.
 */
test("Bugün ekranı: kartlar, grafik, tahsilat penceresi ve rol bazlı içerik", async ({ owner, ownerPage: page, visit, check, pageAs }) => {
  const product = await createProduct(owner, { name: "Kalem", salePrice: 50, initialStock: 2, criticalStockLevel: 5 });
  const contact = await createContact(owner, { firstName: "Ali", lastName: "Can" });
  await createSale(owner, [line(product.id, 1)], [{ method: "Cash", amount: 50 }]);
  await addDebit(owner, contact.id, 400, -3);

  await visit(page, "/panel", "Bugün · Sahip");
  const summary = page.getByRole("region", { name: "Özet" });
  await expect(summary).toContainText("Bugünkü satış");
  await expect(summary).toContainText("50,00 TL");
  await expect(summary).toContainText("Nakit 50,00 TL");
  await expect(summary).toContainText("Vadesi gelen alacak");
  await expect(summary).toContainText("400,00 TL");
  await expect(summary).toContainText("Kritik stok");
  await expect(page.getByLabel("Son 30 gün satış grafiği")).toBeVisible();
  await expect(page.getByText("Kasa ve banka")).toBeVisible();
  await expect(page.getByRole("link", { name: /Vadesi gelen alacak/ })).toHaveAttribute("href", "/panel/cari-kasa?sekme=vadeler");

  // Tahsilat al: vadeler sekmesine gitmez, müşteri seçicili pencere açılır; tutar bakiyeyle dolar.
  await page.getByRole("region", { name: "Hızlı işlemler" }).getByRole("button", { name: "Tahsilat al" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByPlaceholder("Ad veya telefon ara").fill("Ali");
  await dialog.getByRole("button", { name: /Ali Can/ }).click();
  await expect(dialog.getByLabel("Alınan tutar (TL)")).toHaveValue("400,00");
  await check(page, "Bugün · tahsilat penceresi");
  await dialog.getByRole("button", { name: "Tahsilatı kaydet" }).click();
  await expect(toast(page, /Tahsilat kaydedildi/)).toBeVisible();
  await expect(page).toHaveURL(/\/panel$/);
  await expect(summary.getByRole("link", { name: /Bugünkü tahsilat/ })).toContainText("400,00 TL");

  // Personel: kendi satışları ve kritik stok; finans kartları yok.
  const staff = await addMember(owner, "Staff", "Personel Ece");
  await createSale(staff, [line(product.id, 1)], [{ method: "Card", amount: 50 }]);
  const staffPage = await pageAs(staff);
  await staffPage.goto("/panel");
  await check(staffPage, "Bugün · Personel");
  const staffSummary = staffPage.getByRole("region", { name: "Özet" });
  await expect(staffSummary).toContainText("Bugünkü satışlarım");
  await expect(staffSummary).toContainText("Kart 50,00 TL");
  await expect(staffSummary).not.toContainText("Nakit");
  await expect(staffSummary).not.toContainText("Bugünkü tahsilat");
  await expect(staffSummary).not.toContainText("Vadesi gelen alacak");
  await expect(staffPage.getByText("Kasa ve banka")).toHaveCount(0);
  await expect(staffPage.getByRole("region", { name: "Hızlı işlemler" }).getByRole("button", { name: "Tahsilat al" })).toHaveCount(0);

  // Mali Müşavir: her şeyi görür, hızlı işlem yok.
  const accountant = await addMember(owner, "Accountant", "Müşavir Can");
  const accountantPage = await pageAs(accountant);
  await accountantPage.goto("/panel");
  await check(accountantPage, "Bugün · Mali Müşavir");
  await expect(accountantPage.getByRole("region", { name: "Özet" })).toContainText("Bugünkü tahsilat");
  await expect(accountantPage.getByRole("region", { name: "Hızlı işlemler" })).toHaveCount(0);
});
