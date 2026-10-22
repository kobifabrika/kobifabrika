import { addMember, createContact, createProduct, createSale, line } from "./support/api";
import { accounts, addDebit, addExpense, categories, istanbulDay } from "./support/finance";
import { mainMenu } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 4 raporlar: satışlar (kırılım), en çok satan (sıralama), alacaklar (yaşlandırma), gelir-gider; tarih ön ayarları ve
 * özel aralık; Excel indirme. Personel menüde Raporlar'ı görmez, adresle girerse yetki mesajı.
 */
test("raporlar: dört rapor, aralık, Excel ve yetki", async ({ owner, ownerPage: page, visit, check, pageAs }, info) => {
  const pen = await createProduct(owner, { name: "Kalem", salePrice: 10 });
  const book = await createProduct(owner, { name: "Kitap", salePrice: 200 });
  const contact = await createContact(owner, { firstName: "Ali", lastName: "Can" });
  await createSale(owner, [line(pen.id, 10), line(book.id, 1)], [{ method: "Cash", amount: 300 }]);
  await addDebit(owner, contact.id, 750, -40, -60);
  const ids = await accounts(owner);
  await addExpense(owner, { categoryId: (await categories(owner))["Kira"], amount: 1000, cashAccountId: ids["Banka"].id });

  await visit(page, "/panel/raporlar", "Rapor · satışlar");
  const sales = page.getByRole("region", { name: "Satış raporu" });
  await expect(sales).toContainText("300 TL");
  await page.getByRole("group", { name: "Kırılım" }).getByRole("button", { name: "Ay" }).click();
  await expect(page).toHaveURL(/kirilim=ay/);
  await page.getByRole("group", { name: "Tarih aralığı" }).getByRole("button", { name: "Bu yıl" }).click();
  await expect(sales).toContainText("300 TL");
  await page.getByRole("group", { name: "Tarih aralığı" }).getByRole("button", { name: "Özel aralık" }).click();
  await page.getByLabel("Başlangıç").fill(istanbulDay(-60));
  await page.getByLabel("Bitiş").fill(istanbulDay(-31));
  await expect(page.getByText("Bu aralıkta satış yok.")).toBeVisible();
  await check(page, "Rapor · özel aralık");
  await page.getByRole("group", { name: "Tarih aralığı" }).getByRole("button", { name: "Bu ay" }).click();

  const excel = await page.getByRole("link", { name: "Excel'e aktar" }).getAttribute("href");
  const file = await page.request.get(excel!);
  expect(file.status()).toBe(200);
  expect(file.headers()["content-disposition"]).toContain("rapor-satislar");

  // En çok satan: adede göre Kalem, tutara göre Kitap
  await page.getByRole("navigation", { name: "Raporlar" }).getByRole("link", { name: "En çok satan" }).click();
  const top = page.getByRole("region", { name: "En çok satan" });
  await expect(top.getByText(/^1\. Kalem$/).filter({ visible: true })).toBeVisible();
  await page.getByRole("group", { name: "Sıralama" }).getByRole("button", { name: "Tutara göre" }).click();
  await expect(top.getByText(/^1\. Kitap$/).filter({ visible: true })).toBeVisible();
  await check(page, "Rapor · en çok satan");

  // Alacaklar: 40 gün gecikmiş → 31-60 kovası
  await page.getByRole("navigation", { name: "Raporlar" }).getByRole("link", { name: "Alacaklar" }).click();
  const receivables = page.getByRole("region", { name: "Alacaklar" });
  await expect(receivables).toContainText("Ali Can");
  await expect(receivables).toContainText("750 TL");
  await check(page, "Rapor · alacaklar");

  // Gelir-gider
  await page.getByRole("navigation", { name: "Raporlar" }).getByRole("link", { name: "Gelir-gider" }).click();
  const incomeExpense = page.getByRole("region", { name: "Gelir-gider", exact: true });
  await expect(incomeExpense).toContainText("Satışlar");
  await expect(incomeExpense).toContainText("Kira");
  await expect(incomeExpense).toContainText("-700 TL");
  await check(page, "Rapor · gelir-gider");
  const ieExcel = await page.getByRole("link", { name: "Excel'e aktar" }).getAttribute("href");
  expect((await page.request.get(ieExcel!)).status()).toBe(200);

  // Personel: menüde Raporlar yok, adresle girince yetki mesajı.
  const staff = await addMember(owner, "Staff", "Personel Ece");
  const staffPage = await pageAs(staff);
  await staffPage.goto("/panel");
  const menu = await mainMenu(staffPage, info);
  await expect(menu.getByRole("link", { name: "Raporlar" })).toHaveCount(0);
  await staffPage.goto("/panel/raporlar");
  await expect(staffPage.getByText("Bu sayfayı görme yetkiniz yok")).toBeVisible();
});
