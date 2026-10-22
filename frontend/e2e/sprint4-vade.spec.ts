import type { Locator, Page, TestInfo } from "@playwright/test";

import { createContact, createProduct } from "./support/api";
import { addDebit, istanbulDay, setIban, trDay } from "./support/finance";
import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

/** Ödeme paneli: masaüstünde sağdaki sütun, telefonda "Ödeme al" ile açılan alt sayfa. */
async function checkout(page: Page, info: TestInfo): Promise<Locator> {
  if (isDesktop(info)) return page.locator("aside").filter({ hasText: "Satışı tamamla" });
  await page.getByRole("button", { name: "Ödeme al" }).click();
  return page.getByRole("dialog");
}

/**
 * Sprint 4 veresiye ve vade: satışta vade alanı (varsayılan bugün + 30), vade listesi sekmeleri, hatırlatma penceresi
 * (WhatsApp/SMS hazır linkler, tıklama kaydı keepalive ile), müşteri zaman çizelgesi, kısmi tahsilat. Telefon WebKit dahil.
 */
test("veresiye vadesi, vade listesi ve hatırlatma", async ({ owner, ownerPage: page, visit, check }, info) => {
  await setIban(owner);
  await createProduct(owner, { name: "Defter", salePrice: 120 });
  const contact = await createContact(owner, { firstName: "Ayşe", lastName: "Yılmaz", phone: "0532 123 45 67" });
  await addDebit(owner, contact.id, 500, -5); // 5 gün gecikmiş

  // Veresiye satış: vade alanı satış ekranında, varsayılan bugün + 30
  await visit(page, `/panel/satislar/yeni?musteri=${contact.id}`, "Hızlı satış · müşteri");
  await page.getByRole("searchbox", { name: "Ürün ara" }).fill("Defter");
  await page.getByRole("list", { name: "Arama sonuçları" }).getByRole("button", { name: /Defter/ }).click();
  const pay = await checkout(page, info);
  await pay.getByRole("radio", { name: "Veresiye" }).click();
  const due = pay.getByLabel("Vade");
  await expect(due).toHaveValue(istanbulDay(30));
  await due.fill(istanbulDay(45));
  await check(page, "Hızlı satış · veresiye vadesi");
  await pay.getByRole("button", { name: /Satışı tamamla/ }).click();
  await expect(page.getByText(/SAT-\d{4}-\d{6} kaydedildi/)).toBeVisible();

  // Vade listesi: gecikmiş sekmesi
  await visit(page, "/panel/cari-kasa?sekme=vadeler", "Vadeler · gecikmiş");
  await expect(page.getByRole("tab", { name: /Gecikmiş \(1\)/ })).toBeVisible();
  const row = page.getByRole("listitem").filter({ hasText: "Ayşe Yılmaz" });
  await expect(row).toContainText("5 gün gecikti");
  await expect(row).toContainText("500 TL");
  await expect(row).toContainText("Hatırlatma gönderilmedi");

  // İleri tarihli sekmesinde satışın vadesi
  await page.getByRole("tab", { name: /İleri tarihli/ }).click();
  await expect(page.getByRole("listitem").filter({ hasText: "Ayşe Yılmaz" })).toContainText(`Vade ${trDay(istanbulDay(45))}`);
  await page.getByRole("tab", { name: /Gecikmiş/ }).click();

  // Hatırlat: pencere açılırken mesaj hazırlanır; linkler tıklanmadan önce hazır.
  await page.getByRole("listitem").filter({ hasText: "Ayşe Yılmaz" }).getByRole("button", { name: "Hatırlat" }).click();
  const sheet = page.getByRole("dialog");
  const whatsapp = sheet.getByRole("link", { name: "WhatsApp" });
  await expect(whatsapp).toHaveAttribute("href", /^https:\/\/wa\.me\/905321234567\?text=/);
  const sms = sheet.getByRole("link", { name: "SMS" });
  await expect(sms).toHaveAttribute("href", /^sms:\+905321234567\?&body=/);
  await expect(sheet.getByRole("button", { name: "Metni kopyala" })).toBeVisible();
  await expect(sheet.getByLabel("Mesaj metni")).toContainText("500 TL ödemeniz bulunuyor");
  await expect(sheet.getByLabel("Mesaj metni")).toContainText("/odeme/");
  await check(page, "Hatırlatma penceresi");

  // Test ortamında WhatsApp/SMS uygulamasına gidilmez; tıklama kaydı (keepalive) yine gider.
  await page.evaluate(() =>
    document.addEventListener("click", (e) => (e.target as Element).closest("a[href^='sms:'],a[href^='https://wa.me']") && e.preventDefault(), true),
  );
  const logged = page.waitForResponse((r) => /\/api\/receivables\/[^/]+\/reminders$/.test(r.url()) && r.request().method() === "POST");
  await sms.click();
  expect((await logged).status()).toBe(204);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("listitem").filter({ hasText: "Ayşe Yılmaz" })).toContainText("Son hatırlatma");

  // Kısmi tahsilat: gecikmiş tutar azalır
  await page.getByRole("listitem").filter({ hasText: "Ayşe Yılmaz" }).getByRole("button", { name: "Tahsilat al" }).click();
  await page.getByRole("dialog").getByLabel("Alınan tutar (TL)").fill("200");
  await page.getByRole("dialog").getByRole("button", { name: "Tahsilatı kaydet" }).click();
  await expect(toast(page, /Tahsilat kaydedildi/)).toBeVisible();
  await expect(page.getByRole("listitem").filter({ hasText: "Ayşe Yılmaz" })).toContainText("300 TL");

  // Müşteri zaman çizelgesi
  await visit(page, `/panel/musteriler/${contact.id}?sekme=zaman`, "Müşteri · zaman çizelgesi");
  await expect(page.getByText("Hatırlatma gönderildi (SMS)")).toBeVisible();
  await visit(page, `/panel/musteriler/${contact.id}?sekme=cari`, "Müşteri · cari (vade özeti)");
  await expect(page.getByText(/Gecikmiş 300\sTL/)).toBeVisible();
});
