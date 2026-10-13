import type { Page } from "@playwright/test";

import { createContact, createProduct, line } from "./support/api";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

/** "Paylaş" penceresini açar, gösterilen linki döner ve pencereyi kapatır. */
async function shareLink(page: Page): Promise<string> {
  await page.getByRole("button", { name: "Paylaş" }).click();
  const dialog = page.getByRole("dialog");
  const url = (await dialog.getByText(/\/teklif\//).textContent())!.trim();
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  return url;
}

/** Sprint 3 teklif: hazırla (yüzde indirim) → paylaş (iki kez aynı link) → müşteri onayı → bildirim → satışa dönüştür. */
test("teklif: hazırla, paylaş, müşteri onayı, bildirim, satışa dönüştür", async ({
  owner,
  ownerPage: page,
  newPublicPage,
  visit,
  check,
}) => {
  const contact = await createContact(owner, { firstName: "Ayşe", lastName: "Yılmaz" });
  await createProduct(owner, { name: "Beyaz peynir", salePrice: 320, unit: "Kg", vatRate: 1 });

  await visit(page, `/panel/teklifler/yeni?musteri=${contact.id}`, "Yeni teklif");
  await expect(page.getByText("Ayşe Yılmaz").first()).toBeVisible();
  await page.getByRole("searchbox", { name: "Ürün ara" }).fill("Peynir");
  await page.getByRole("button", { name: /Beyaz peynir/ }).click();
  await page.getByRole("radiogroup", { name: "Toplam indirim tipi" }).getByRole("radio", { name: "%" }).click();
  await page.getByLabel(/Toplam indirim \(/).fill("10");
  await check(page, "Yeni teklif · dolu");
  await page.getByRole("button", { name: "Teklifi hazırla" }).click();
  await expect(page).toHaveURL(/\/panel\/teklifler\/[0-9a-f-]{36}$/);
  const quoteId = page.url().split("/").pop()!;
  await expect(page.getByText("288,00 TL").first()).toBeVisible();
  await check(page, "Teklif detayı · taslak");

  // Paylaş: iki kez aynı link
  const first = await shareLink(page);
  const second = await shareLink(page);
  expect(second).toBe(first);

  // Müşteri ayrı bağlamda onaylar.
  const customer = await newPublicPage();
  await visit(customer, first, "Teklif · müşteri");
  await customer.getByRole("button", { name: /Teklifi onayla/ }).click();
  await expect(customer.getByText(/Teklifi onayladınız/)).toBeVisible();
  await check(customer, "Teklif · müşteri onayladı");

  // Bildirim
  await page.reload();
  await page.getByRole("button", { name: /Bildirimler, \d+ okunmamış/ }).click();
  await expect(page.getByText("Ayşe Yılmaz teklifi onayladı")).toBeVisible();
  await page.keyboard.press("Escape");

  // Satışa dönüştür (varsayılan veresiye)
  await page.goto(`/panel/teklifler/${quoteId}`);
  await page.getByRole("button", { name: "Satışa dönüştür" }).click();
  // Veresiye seçili: vade alanı varsayılan tarihle (bugün + 30) görünür; başka bir tarih seçilir.
  const dueInput = page.getByRole("dialog").getByLabel("Vade");
  await expect(dueInput).toHaveValue(/^\d{4}-\d{2}-\d{2}$/);
  await check(page, "Satışa dönüştür");
  const due = new Date(Date.now() + 45 * 86_400_000).toISOString().slice(0, 10);
  await dueInput.fill(due);
  await page.getByRole("button", { name: /Satışı kaydet/ }).click();
  await expect(page).toHaveURL(/\/panel\/satislar\/[0-9a-f-]{36}$/);
  const saleId = page.url().split("/").pop()!;
  expect((await owner.get<{ dueDate: string | null }>(`/api/sales/${saleId}`)).dueDate).toBe(due);
  await expect(page.getByText(/Teklif TKL-\d{4}-\d{4}/).first()).toBeVisible();
  expect((await owner.get<{ balance: number }>(`/api/contacts/${contact.id}`)).balance).toBe(288);
  const quote = await owner.get<{ status: string }>(`/api/quotes/${quoteId}`);
  expect(quote.status).toBe("Converted");
});

test("süresi dolmuş teklifte Paylaş önce uzatma onayı sorar (Linki yenile: backend testi)", async ({ owner, ownerPage: page, check }) => {
  const contact = await createContact(owner, { firstName: "Can", lastName: "Er" });
  const product = await createProduct(owner, { name: "Masa", salePrice: 1500 });
  const today = new Date();
  const iso = (d: Date) => d.toISOString().slice(0, 10);
  const start = new Date(today.getTime() - 40 * 86_400_000);
  const end = new Date(today.getTime() - 25 * 86_400_000);
  const quote = await owner.post<{ id: string; validUntil: string; isExpired: boolean }>("/api/quotes", {
    contactId: contact.id,
    date: iso(start),
    validUntil: iso(end),
    lines: [line(product.id)],
    discount: null,
    notes: null,
  });
  expect(quote.isExpired).toBe(true);

  await page.goto(`/panel/teklifler/${quote.id}`);
  await page.getByRole("button", { name: "Paylaş" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("Teklifin süresi doldu. Geçerliliği 15 gün uzatıp yeni link oluşturulsun mu?")).toBeVisible();
  await check(page, "Teklif · süresi doldu onayı");

  // Vazgeç: tarih değişmez
  await dialog.getByRole("button", { name: "Vazgeç" }).click();
  expect((await owner.get<{ validUntil: string }>(`/api/quotes/${quote.id}`)).validUntil).toBe(quote.validUntil);

  // Evet, uzat: yeni link, geçerlilik uzar, aktivite günlüğüne düşer
  await page.getByRole("button", { name: "Paylaş" }).click();
  await dialog.getByRole("button", { name: "Evet, uzat" }).click();
  await expect(toast(page, /Geçerlilik .* tarihine uzatıldı/)).toBeVisible();
  await expect(dialog.getByText(/\/teklif\//)).toBeVisible();
  const extended = await owner.get<{ validUntil: string; isExpired: boolean; total: number }>(`/api/quotes/${quote.id}`);
  expect(extended.isExpired).toBe(false);
  expect(extended.total).toBe(1500);
  const logs = await owner.get<{ items: unknown[] }>("/api/audit-logs?action=QuoteExtended");
  expect(logs.items).toHaveLength(1);
  await page.keyboard.press("Escape");
  await page.goto("/panel/ayarlar/aktivite");
  await expect(page.getByText("Teklifin geçerliliğini uzattı").filter({ visible: true }).first()).toBeVisible();
});

test("yüzdelik indirimli teklif düzenlemede % olarak açılır", async ({ owner, ownerPage: page, visit }) => {
  const contact = await createContact(owner, { firstName: "Elif", lastName: "Su" });
  const product = await createProduct(owner, { name: "Lamba", salePrice: 400 });
  const quote = await owner.post<{ id: string }>("/api/quotes", {
    contactId: contact.id,
    date: null,
    validUntil: null,
    lines: [line(product.id, 2, { discount: { type: "Percent", value: 15 } })],
    discount: { type: "Percent", value: 12.5 },
    notes: null,
  });

  await visit(page, `/panel/teklifler/${quote.id}/duzenle`, "Teklif düzenle · yüzde indirim");
  const totalType = page.getByRole("radiogroup", { name: "Toplam indirim tipi" });
  await expect(totalType.getByRole("radio", { name: "%" })).toHaveAttribute("aria-checked", "true");
  await expect(page.getByLabel(/Toplam indirim \(/)).toHaveValue("12,5");
  await expect(page.getByRole("list", { name: "Sepet" })).toContainText("indirim %15");

  // Değiştirmeden kaydet: tutar aynı kalır (yüzde, TL'ye dönmez)
  await page.getByRole("button", { name: "Teklifi kaydet" }).click();
  await expect(toast(page, "Teklif güncellendi.")).toBeVisible();
  const saved = await owner.get<{ discount: { type: string; value: number }; total: number }>(`/api/quotes/${quote.id}`);
  expect(saved.discount).toEqual({ type: "Percent", value: 12.5 });
  expect(saved.total).toBe(595);
});
