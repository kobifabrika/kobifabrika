import type { Page } from "@playwright/test";

import { createContact, type ApiUser } from "./support/api";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

const IBAN = "TR33 0006 1005 1978 6457 8413 26";

/** Kartın cari sekmesinde yeni tahsilat linki oluşturur, linki döner. */
async function createLink(page: Page, contactId: string, amount: string): Promise<string> {
  await page.goto(`/panel/musteriler/${contactId}?sekme=cari`);
  await page.getByRole("button", { name: "Tahsilat linki" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("İstenen tutar (TL)").fill(amount);
  await dialog.getByRole("button", { name: "Link oluştur" }).click();
  const url = (await dialog.getByText(/\/odeme\//).textContent())!.trim();
  await page.keyboard.press("Escape");
  return url;
}

/** Cari ve Kasa → Tahsilat linkleri satırı (tutara göre). */
function requestRow(page: Page, amount: string) {
  return page.getByRole("listitem").filter({ hasText: amount }).first();
}

async function owing(owner: ApiUser, contactId: string, amount: number) {
  await owner.post(`/api/contacts/${contactId}/ledger`, { kind: "Debit", amount, date: null, dueDate: null, description: "Veresiye" });
}

/** Sprint 2: IBAN → link → iki kez paylaşınca aynı link → müşteri "Ödedim" → bildirim → onayla → bakiye düşer. */
test("tahsilat linki: IBAN, paylaş, Ödedim, bildirim, onay", async ({
  owner,
  ownerPage: page,
  newPublicPage,
  visit,
  check,
  guard,
}) => {
  const contact = await createContact(owner, { firstName: "Selin", lastName: "Ak" });
  await owing(owner, contact.id, 1000);

  // IBAN yokken link oluşturulamaz → Ayarlar'a yönlendirir.
  await page.goto(`/panel/musteriler/${contact.id}?sekme=cari`);
  await page.getByRole("button", { name: "Tahsilat linki" }).click();
  await page.getByRole("dialog").getByLabel("İstenen tutar (TL)").fill("300");
  guard.allow(409, /\/payment-requests$/);
  await page.getByRole("dialog").getByRole("button", { name: "Link oluştur" }).click();
  await expect(page.getByText("Tahsilat linki için önce işletmenin IBAN'ını girin.")).toBeVisible();
  await page.getByRole("link", { name: "Ayarlar'a git" }).click();
  await expect(page).toHaveURL(/\/panel\/ayarlar/);
  await page.getByLabel("IBAN").fill(IBAN);
  const ibanCard = page.locator("form").filter({ has: page.getByLabel("IBAN") });
  await ibanCard.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Ödeme bilgileri kaydedildi.")).toBeVisible();

  // Link oluştur; "Paylaş" iki kez aynı linki verir.
  const url = await createLink(page, contact.id, "300");
  expect(url).toMatch(/\/odeme\//);
  await visit(page, "/panel/cari-kasa?sekme=tahsilat-linkleri", "Tahsilat linkleri");
  for (let i = 0; i < 2; i++) {
    await requestRow(page, "300,00").getByRole("button", { name: "Paylaş" }).click();
    await expect(page.getByRole("dialog").getByText(url)).toBeVisible();
    await page.keyboard.press("Escape");
  }

  // Müşteri ayrı bağlamda (giriş yok) "Ödedim" der.
  const customer = await newPublicPage();
  await visit(customer, url, "Tahsilat linki · müşteri");
  await expect(customer.getByText("300,00").first()).toBeVisible();
  await customer.getByLabel("Not (isteğe bağlı)").fill("Garanti'den gönderdim");
  await customer.getByRole("button", { name: "Ödedim" }).click();
  await expect(customer.getByText("Teşekkürler, bildiriminiz iletildi.", { exact: false })).toBeVisible();
  await check(customer, "Tahsilat linki · Ödedim sonrası");

  // Panelde bildirim
  await page.reload();
  await page.getByRole("button", { name: /Bildirimler, \d+ okunmamış/ }).click();
  await expect(page.getByText('Selin Ak "Ödedim" dedi')).toBeVisible();
  await check(page, "Bildirimler");
  await page.keyboard.press("Escape");

  // Onayla → tahsilat kaydı, bakiye 1.000 → 700
  await page.goto("/panel/cari-kasa?sekme=tahsilat-linkleri");
  await expect(requestRow(page, "300,00")).toContainText("Müşteri notu: Garanti'den gönderdim");
  await requestRow(page, "300,00").getByRole("button", { name: "Onayla" }).click();
  await check(page, "Ödemeyi onayla");
  await page.getByRole("dialog").getByRole("button", { name: "Onayla" }).click();
  await expect(toast(page, "Ödeme onaylandı; tahsilat kaydedildi.")).toBeVisible();
  const after = await owner.get<{ balance: number }>(`/api/contacts/${contact.id}`);
  expect(after.balance).toBe(700);
  await page.goto(`/panel/musteriler/${contact.id}?sekme=cari`);
  await expect(page.getByText("Güncel bakiye").locator("..")).toContainText("Alacak 700,00 TL");
});

test("tahsilat linki: ödeme gelmedi, iptal ve linki yenile", async ({ owner, ownerPage: page, newPublicPage, guard }) => {
  await owner.put("/api/business/payment-info", { iban: IBAN, ibanHolderName: null });
  const contact = await createContact(owner, { firstName: "Can", lastName: "Er" });
  await owing(owner, contact.id, 500);
  const customer = await newPublicPage();

  // "Ödedim" → "Ödeme gelmedi" → link tekrar ödeme bekler → iptal → link geçersiz.
  const first = await createLink(page, contact.id, "150");
  await customer.goto(first);
  await customer.getByRole("button", { name: "Ödedim" }).click();
  await expect(customer.getByText("Teşekkürler, bildiriminiz iletildi.", { exact: false })).toBeVisible();
  await page.goto("/panel/cari-kasa?sekme=tahsilat-linkleri");
  await requestRow(page, "150,00").getByRole("button", { name: "Ödeme gelmedi" }).click();
  await expect(toast(page, "Link tekrar ödeme bekliyor.")).toBeVisible();
  await customer.reload();
  await expect(customer.getByRole("button", { name: "Ödedim" })).toBeVisible();
  await requestRow(page, "150,00").getByRole("button", { name: "İptal" }).click();
  await expect(toast(page, "Link iptal edildi.")).toBeVisible();
  guard.allow(410, /\/api\/public\/payment-requests\//);
  guard.allow(404, /\/api\/public\/payment-requests\//);
  await customer.reload();
  await expect(customer.getByText(/Bu ödeme bağlantısı artık geçerli değil|Ödeme bağlantısı bulunamadı/)).toBeVisible();

  // "Linki yenile": eski link 404, yenisi açılır.
  const old = await createLink(page, contact.id, "200");
  await page.goto("/panel/cari-kasa?sekme=tahsilat-linkleri");
  await requestRow(page, "200,00").getByRole("button", { name: "Paylaş" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Linki yenile" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Evet, yenile" }).click();
  await expect(toast(page, "Link yenilendi; eski link artık çalışmıyor.")).toBeVisible();
  const renewed = (await page.getByRole("dialog").getByText(/\/odeme\//).textContent())!.trim();
  expect(renewed).not.toBe(old);

  await customer.goto(old);
  await expect(customer.getByText("Ödeme bağlantısı bulunamadı")).toBeVisible();
  await customer.goto(renewed);
  await expect(customer.getByRole("button", { name: "Ödedim" })).toBeVisible();
});
