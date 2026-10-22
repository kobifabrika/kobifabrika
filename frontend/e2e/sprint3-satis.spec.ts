import type { Locator, Page, TestInfo } from "@playwright/test";

import { addMember, createContact, createProduct, createSale, line } from "./support/api";
import { toast } from "./support/nav";
import { expect, isDesktop, test, waitReady } from "./support/test";

/** Ödeme paneli: masaüstünde sağdaki sütun, telefonda "Ödeme al" ile açılan alt sayfa. */
async function checkout(page: Page, info: TestInfo): Promise<Locator> {
  if (isDesktop(info)) return page.locator("aside").filter({ hasText: "Satışı tamamla" });
  await page.getByRole("button", { name: "Ödeme al" }).click();
  return page.getByRole("dialog");
}

/** Barkod okuyucu taklidi: arama kutusuna hızlı yazıp Enter. */
async function scan(page: Page, barcode: string) {
  const search = page.getByRole("searchbox", { name: "Ürün ara" });
  await search.click();
  await page.keyboard.type(barcode, { delay: 5 });
  await page.keyboard.press("Enter");
}

/**
 * Sprint 3 hızlı satış: barkodla sepete ekleme, bölünmüş ödeme (nakit + veresiye), fiş (80 ve 58 mm), müşteri bakiyesi /
 * zaman çizelgesi / son ziyaret, kasa bakiyesi, kritik stok bildirimi.
 */
test("hızlı satış: barkod, bölünmüş ödeme, fiş, bakiye ve kasa", async ({ owner, ownerPage: page, visit, check }, info) => {
  const product = await createProduct(owner, {
    name: "Pamuklu tişört",
    salePrice: 249.9,
    barcode: "8690000000011",
    initialStock: 4,
    criticalStockLevel: 3,
  });
  const contact = await createContact(owner, { firstName: "Ayşe", lastName: "Yılmaz" });

  await visit(page, "/panel/satislar/yeni", "Hızlı satış · boş");
  await scan(page, "8690000000011");
  await expect(toast(page, "Pamuklu tişört eklendi")).toBeVisible();
  await scan(page, "8690000000011");
  await expect(page.getByRole("list", { name: "Sepet" }).getByLabel(/Miktar: 2 adet/)).toBeVisible();
  await check(page, "Hızlı satış · sepet");

  const pay = await checkout(page, info);
  await pay.getByPlaceholder("Ad veya telefon ara").fill("Ayşe");
  await pay.getByRole("button", { name: /Ayşe Yılmaz/ }).click();
  await pay.getByRole("button", { name: "Böl" }).click();
  await pay.getByLabel("1. ödeme tipi").selectOption({ label: "Nakit" });
  await pay.getByLabel("1. ödeme tutarı").fill("200");
  await pay.getByLabel("2. ödeme tipi").selectOption({ label: "Veresiye" });
  await pay.getByLabel("2. ödeme tutarı").fill("299,80");
  await check(page, "Hızlı satış · bölünmüş ödeme");
  await pay.getByRole("button", { name: /Satışı tamamla/ }).click();
  await expect(page.getByText(/SAT-\d{4}-\d{6} kaydedildi/)).toBeVisible();
  await check(page, "Hızlı satış · tamamlandı");

  const sales = await owner.get<{ items: { id: string; no: string; total: number }[] }>("/api/sales");
  const sale = sales.items[0];
  expect(sale.total).toBe(499.8);
  expect((await owner.get<{ balance: number }>(`/api/contacts/${contact.id}`)).balance).toBe(299.8);
  expect((await owner.get<{ stockQuantity: number }>(`/api/products/${product.id}`)).stockQuantity).toBe(2);
  const notes = await owner.get<{ items: { type: string }[] }>("/api/notifications");
  expect(notes.items.some((n) => n.type === "stock.low")).toBe(true);

  // Fiş 80 mm (varsayılan)
  await visit(page, `/panel/satislar/${sale.id}/fis`, "Fiş 80 mm");
  const receipt = page.getByRole("article", { name: `Fiş ${sale.no}` });
  await expect(receipt).toHaveAttribute("data-width", "80");
  await expect(receipt).toContainText("Bilgi fişidir, mali değeri yoktur.");
  await expect(receipt).toContainText("Veresiye");
  await expect(receipt).toContainText("499,80 TL");

  // Ayarlar → 58 mm; fiş dar ve ürün adı kesilmiyor
  await page.goto("/panel/ayarlar");
  await page.getByRole("radiogroup", { name: "Fiş genişliği" }).getByRole("radio", { name: "58 mm" }).click();
  await expect(toast(page, "Fiş genişliği 58 mm.")).toBeVisible();
  await visit(page, `/panel/satislar/${sale.id}/fis`, "Fiş 58 mm");
  await expect(receipt).toHaveAttribute("data-width", "58");
  await expect(receipt).toContainText("Bilgi fişidir, mali değeri yoktur.");
  const box = await receipt.boundingBox();
  expect(Math.round(box!.width)).toBeLessThanOrEqual(220); // 58 mm ≈ 219 px
  const nameFits = await receipt
    .getByText("Pamuklu tişört")
    .evaluate((el) => el.scrollWidth <= el.clientWidth && getComputedStyle(el).textOverflow !== "ellipsis");
  expect(nameFits).toBe(true);
  const pdf = await page.request.get(`http://localhost:5180/api/sales/${sale.id}/receipt.pdf`);
  expect(pdf.headers()["content-type"]).toContain("application/pdf");

  // Müşteri kartı: bakiye, zaman çizelgesi, son ziyaret, satışlar sekmesi
  await visit(page, `/panel/musteriler/${contact.id}`, "Müşteri kartı · satış sonrası");
  await expect(page.getByText("Alacak 299,80 TL").first()).toBeVisible();
  await expect(page.getByText("Son ziyaret").locator("..")).not.toContainText("—");
  await page.goto(`/panel/musteriler/${contact.id}?sekme=zaman`);
  await expect(page.getByText(new RegExp(sale.no)).first()).toBeVisible();
  await page.goto(`/panel/musteriler/${contact.id}?sekme=satislar`);
  await expect(page.getByText(sale.no).first()).toBeVisible();

  // Kasa: nakit kısmı Nakit Kasa'da
  await page.goto("/panel/cari-kasa?sekme=kasa");
  await expect(page.getByRole("listitem").filter({ hasText: "Nakit Kasa" })).toContainText("200 TL");
});

test("Yönetici satışı iptal eder: stok, kasa ve cari ters kayıtla geri alınır", async ({ owner, pageAs, visit }) => {
  const product = await createProduct(owner, { name: "Kahve", salePrice: 100, initialStock: 10 });
  const contact = await createContact(owner, { firstName: "Deniz", lastName: "Kum" });
  const sale = await createSale(
    owner,
    [line(product.id, 3)],
    [
      { method: "Cash", amount: 100 },
      { method: "Credit", amount: 200 },
    ],
    contact.id,
  );
  const manager = await pageAs(await addMember(owner, "Manager"));

  await visit(manager, `/panel/satislar/${sale.id}`, "Satış detayı · Yönetici");
  await manager.getByRole("button", { name: "Satışı iptal et" }).click();
  await manager.getByLabel("Neden (isteğe bağlı)").fill("Müşteri vazgeçti");
  await manager.getByRole("dialog").getByRole("button", { name: /iptal et/i }).click();
  await expect(toast(manager, `${sale.no} iptal edildi.`)).toBeVisible();
  await expect(manager.getByText(/ters kayıtla geri alındı|İptal/).first()).toBeVisible();

  expect((await owner.get<{ stockQuantity: number }>(`/api/products/${product.id}`)).stockQuantity).toBe(10);
  expect((await owner.get<{ balance: number }>(`/api/contacts/${contact.id}`)).balance).toBe(0);
  const accounts = await owner.get<{ name: string; balance: number }[]>("/api/cash-accounts");
  expect(accounts.find((a) => a.name === "Nakit Kasa")?.balance).toBe(0);
  const ledger = await owner.get<{ items: { refType: string }[] }>(`/api/contacts/${contact.id}/ledger`);
  expect(ledger.items.map((i) => i.refType)).toEqual(expect.arrayContaining(["Sale", "SaleCancel"]));
});

test("Personel: alış fiyatı yok, sadece kendi satışları, iptal düğmesi yok", async ({ owner, pageAs, visit, guard }) => {
  const product = await createProduct(owner, { name: "Çanta", salePrice: 500, purchasePrice: 300 });
  const ownerSale = await createSale(owner, [line(product.id)], [{ method: "Cash", amount: 500 }]);
  const staffUser = await addMember(owner, "Staff");
  const staffSale = await createSale(staffUser, [line(product.id, 2)], [{ method: "Card", amount: 1000 }]);
  const staff = await pageAs(staffUser);

  await visit(staff, `/panel/urunler/${product.id}`, "Ürün kartı · Personel");
  await expect(staff.getByText("Alış fiyatı")).toHaveCount(0);
  await expect(staff.getByText("300 TL")).toHaveCount(0);

  await visit(staff, "/panel/satislar", "Satışlar · Personel");
  await expect(staff.getByText(staffSale.no).filter({ visible: true }).first()).toBeVisible();
  await expect(staff.getByText(ownerSale.no)).toHaveCount(0);

  await visit(staff, `/panel/satislar/${staffSale.id}`, "Satış detayı · Personel");
  await expect(staff.getByRole("button", { name: "Satışı iptal et" })).toHaveCount(0);

  guard.allow(404, new RegExp(`/api/sales/${ownerSale.id}`));
  await staff.goto(`/panel/satislar/${ownerSale.id}`);
  await expect(staff.getByText(/bulunamadı/).first()).toBeVisible();
});

test("Ctrl+K: ürün adı, barkod ve satış numarasıyla arama", async ({ owner, ownerPage: page }, info) => {
  const product = await createProduct(owner, { name: "Lavanta sabunu", salePrice: 45, barcode: "8690000000777" });
  const sale = await createSale(owner, [line(product.id)], [{ method: "Cash", amount: 45 }]);
  await page.goto("/panel");
  await waitReady(page);

  const open = async () => {
    if (isDesktop(info)) await page.keyboard.press("Control+k");
    else await page.getByRole("button", { name: "Ara", exact: true }).click();
    return page.getByPlaceholder("Müşteri, ürün, satış veya sayfa ara…");
  };

  let input = await open();
  await input.fill("Lavanta");
  await expect(page.getByRole("option", { name: /Lavanta sabunu/ })).toBeVisible();
  await input.fill("8690000000777");
  await expect(page.getByRole("option", { name: /Lavanta sabunu/ })).toBeVisible();
  await input.fill(sale.no);
  await page.getByRole("option", { name: new RegExp(sale.no) }).click();
  await expect(page).toHaveURL(new RegExp(`/panel/satislar/${sale.id}`));

  input = await open();
  await input.fill("ayar");
  await expect(page.getByRole("option", { name: /Ayarlar/ }).first()).toBeVisible();
});
