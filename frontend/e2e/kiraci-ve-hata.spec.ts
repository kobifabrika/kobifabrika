import { addMember, createContact, createProduct, createQuote, createSale, line, ownerWithBusiness } from "./support/api";
import { apiUrl } from "./scripts/env.mjs";
import { expect, test } from "./support/test";

/** Kiracı ayrımı: B işletmesinin kullanıcısı A'nın kayıt adreslerini açınca "bulunamadı" görür; API 404 döner. */
test("B işletmesi A'nın müşteri, cari, ürün, satış ve teklif adreslerinde 404 görür", async ({
  owner: a,
  pageAs,
  guard,
  check,
}) => {
  const contact = await createContact(a, { firstName: "Gizli", lastName: "Müşteri" });
  const supplier = await createContact(a, { firstName: "Gizli", lastName: "Tedarikçi", isCustomer: false, isSupplier: true });
  const product = await createProduct(a, { name: "Gizli ürün", salePrice: 10 });
  const sale = await createSale(a, [line(product.id)], [{ method: "Credit", amount: 10 }], contact.id);
  const quote = await createQuote(a, contact.id, [line(product.id)]);

  const b = await ownerWithBusiness({ name: "B İşletmesi" });
  const bManager = await addMember(b, "Manager");
  guard.allow(404, /localhost:5180\/api\/(contacts|products|sales|quotes)\//);

  for (const user of [b, bManager]) {
    const page = await pageAs(user);
    const urls = [
      `/panel/musteriler/${contact.id}`,
      `/panel/musteriler/${contact.id}?sekme=cari`,
      `/panel/musteriler/${contact.id}/duzenle`,
      `/panel/cari-kasa/tedarikciler/${supplier.id}`,
      `/panel/urunler/${product.id}`,
      `/panel/urunler/${product.id}/duzenle`,
      `/panel/satislar/${sale.id}`,
      `/panel/satislar/${sale.id}/fis`,
      `/panel/teklifler/${quote.id}`,
      `/panel/teklifler/${quote.id}/duzenle`,
    ];
    for (const url of urls) {
      await page.goto(url);
      await expect(page.getByText(/bulunamadı/i).first(), url).toBeVisible();
      await expect(page.getByText(/Gizli (Müşteri|Tedarikçi|ürün)/), url).toHaveCount(0);
    }
    await check(page, "Kiracı ayrımı · başka işletmenin kaydı");

    for (const path of [
      `/api/contacts/${contact.id}`,
      `/api/contacts/${contact.id}/ledger`,
      `/api/contacts/${contact.id}/statement.pdf`,
      `/api/products/${product.id}`,
      `/api/sales/${sale.id}`,
      `/api/sales/${sale.id}/receipt.pdf`,
      `/api/quotes/${quote.id}`,
      `/api/quotes/${quote.id}/pdf`,
    ]) {
      expect((await user.ctx.get(path)).status(), path).toBe(404);
    }
    // Değiştirme denemesi de 404
    expect((await user.ctx.post(`/api/sales/${sale.id}/cancel`, { data: {} })).status()).toBe(404);
    expect((await user.ctx.post(`/api/quotes/${quote.id}/share`, { data: { sendEmail: false } })).status()).toBe(404);
  }

  // A'nın verisi değişmedi
  expect((await a.get<{ balance: number }>(`/api/contacts/${contact.id}`)).balance).toBe(10);
});

/** API kapalıyken Türkçe hata ve "Tekrar dene"; API dönünce sayfa açılır. */
test("API kapalıyken Türkçe hata ve Tekrar dene", async ({ ownerPage: page, guard, check }) => {
  guard.allow("network");
  await page.route(`${apiUrl}/**`, (route) => route.abort("connectionrefused"));
  await page.goto("/panel/musteriler");
  await expect(page.getByText("Panel açılamadı")).toBeVisible({ timeout: 60_000 });
  await expect(page.getByText(/Sunucuya ulaşılamıyor/).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Tekrar dene" })).toBeVisible();
  await check(page, "Hata · API kapalı");

  await page.unroute(`${apiUrl}/**`);
  await page.getByRole("button", { name: "Tekrar dene" }).click();
  await expect(page.getByRole("heading", { name: "Müşteriler" })).toBeVisible();

  // Panel açıkken API düşerse sayfa içinde aynı hata ve "Tekrar dene".
  await page.route(`${apiUrl}/api/products**`, (route) => route.abort("connectionrefused"));
  await page.goto("/panel/urunler");
  await expect(page.getByText(/Sunucuya ulaşılamıyor/).first()).toBeVisible({ timeout: 60_000 });
  await expect(page.getByRole("button", { name: "Tekrar dene" })).toBeVisible();
  await check(page, "Hata · liste yüklenemedi");
  await page.unroute(`${apiUrl}/api/products**`);
  await page.getByRole("button", { name: "Tekrar dene" }).click();
  await expect(page.getByRole("button", { name: "Tekrar dene" })).toHaveCount(0);
});
