import type { Locator, Page, TestInfo } from "@playwright/test";

import { createContact } from "./support/api";
import { createService, createStaff, openAllDay, pastSlot } from "./support/appointments";
import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

test.use({ ownerSector: "BeautyCare" });

/** Ödeme paneli: masaüstünde sağdaki sütun, telefonda "Ödeme al" ile açılan alt sayfa. */
async function checkout(page: Page, info: TestInfo): Promise<Locator> {
  if (isDesktop(info)) return page.locator("aside").filter({ hasText: "Satışı tamamla" });
  await page.getByRole("button", { name: "Ödeme al" }).click();
  return page.getByRole("dialog");
}

/**
 * Sprint 5B paketler: paket tanımla → hızlı satıştan sat (Paketler grubu, müşteri zorunlu) → yeni randevu formunda "Paketten kullan"
 * → paketli randevu tamamla (seans düşer) → satışa çevir (0 TL satırı) → müşteri kartında "1 seans kaldı" ve Paketler sekmesi.
 */
test("paket: tanımla, sat, randevuda kullan, satışa çevir, son seans uyarısı", async ({ owner, ownerPage: page, visit, check }, info) => {
  await openAllDay(owner);
  const service = await createService(owner, "Lazer", 30, 500);
  await createStaff(owner, "Ayşe Usta");
  const contact = await createContact(owner, { firstName: "Elif", lastName: "Demir" });

  // Paket tanımı.
  await visit(page, "/panel/urunler?sekme=paketler", "Ürünler · Paketler · boş");
  await expect(page.getByText("Henüz paket yok")).toBeVisible();
  await page.getByRole("link", { name: "Yeni paket" }).click();
  await expect(page.getByRole("heading", { name: "Yeni paket" })).toBeVisible();
  await page.getByLabel("Paket adı").fill("10 seans lazer");
  await page.getByLabel("Hizmet").selectOption({ label: "Lazer" });
  await page.getByLabel("Seans sayısı").fill("2");
  await page.getByLabel("Geçerlilik (gün)").fill("90");
  await page.getByLabel("Fiyat (KDV dahil)").fill("3.000");
  await check(page, "Yeni paket");
  await page.getByRole("button", { name: "Paketi tanımla" }).click();
  await expect(toast(page, "Paket tanımlandı.")).toBeVisible();
  await expect(page).toHaveURL(/\/panel\/urunler\?sekme=paketler/);
  await expect(page.getByText("Lazer · 2 seans · 90 gün geçerli")).toBeVisible();
  await check(page, "Ürünler · Paketler");

  // Hızlı satış: arama "Paketler" grubunda paket; müşteri kartından gelince müşteri seçili.
  await visit(page, `/panel/satislar/yeni?musteri=${contact.id}`, "Hızlı satış · paket");
  await page.getByLabel("Ürün ara").fill("lazer");
  await page.getByRole("list", { name: "Paketler" }).getByRole("button", { name: /10 seans lazer/ }).click();
  await expect(toast(page, "10 seans lazer eklendi")).toBeVisible();
  const pay = await checkout(page, info);
  await expect(pay.getByText("Elif Demir")).toBeVisible();
  await pay.getByRole("radio", { name: "Nakit" }).click();
  await pay.getByRole("button", { name: /Satışı tamamla/ }).click();
  await expect(page.getByText(/SAT-\d{4}-\d{6} kaydedildi/)).toBeVisible();
  const packages = await owner.get<{ items: { id: string; remaining: number }[] }>(`/api/contacts/${contact.id}/packages`);
  expect(packages.items).toHaveLength(1);
  expect(packages.items[0]!.remaining).toBe(2);

  // Yeni randevu formunda "Paketten kullan" (tek paket önceden seçili).
  await visit(page, `/panel/randevular?yeni=1&musteri=${contact.id}`, "Yeni randevu · paket");
  const form = page.getByRole("dialog");
  await form.getByLabel("Hizmet").selectOption({ label: "Lazer (30 dk)" });
  await expect(form.getByLabel("Paketten kullan").locator("option:checked")).toHaveText(/10 seans lazer · kalan 2\/2/);
  await check(page, "Yeni randevu · paketten kullan");
  await form.getByRole("button", { name: "Vazgeç" }).click();

  // Paketli randevu (geçmiş saat) → Tamamlandı → seans düşer → Satışa çevir: 0 TL satırı.
  const staff = await owner.get<{ id: string }[]>("/api/staff");
  const slot = pastSlot(90);
  const booked = await owner.post<{ appointment: { id: string } }>("/api/appointments", {
    contactId: contact.id,
    staffMemberId: staff[0]!.id,
    productId: service.id,
    startAt: slot.iso,
    endAt: null,
    note: null,
    customerPackageId: packages.items[0]!.id,
  });
  await visit(page, `/panel/randevular?tarih=${slot.day}&randevu=${booked.appointment.id}`, "Randevu ayrıntısı · paketli");
  const sheet = page.getByRole("dialog");
  await expect(sheet.getByText("10 seans lazer")).toBeVisible();
  await sheet.getByRole("button", { name: "Tamamlandı" }).click();
  await expect(toast(page, "Randevu tamamlandı.")).toBeVisible();
  await expect(sheet.getByText("seans düştü")).toBeVisible();
  await sheet.getByRole("link", { name: "Satışa çevir" }).click();
  await expect(page.getByText("Randevudan satış: Paket: 10 seans lazer (1/2) · Ayşe Usta")).toBeVisible();
  await expect(page.getByRole("list", { name: "Sepet" }).getByText("0,00 TL").first()).toBeVisible();
  await check(page, "Hızlı satış · paketli randevudan");
  const zero = await checkout(page, info);
  await zero.getByRole("button", { name: /Satışı tamamla/ }).click();
  await expect(page.getByText(/SAT-\d{4}-\d{6} kaydedildi/)).toBeVisible();

  // Müşteri kartı: son seans uyarısı ve Paketler sekmesi.
  await visit(page, `/panel/musteriler/${contact.id}`, "Müşteri kartı · son seans uyarısı");
  await expect(page.getByText("10 seans lazer paketinde 1 seans kaldı.")).toBeVisible();
  await page.getByRole("link", { name: "Paketler" }).click();
  await expect(page.getByText("1/2 seans kullanıldı · kalan 1")).toBeVisible();
  await expect(page.getByText("Aktif", { exact: true })).toBeVisible();
  await check(page, "Müşteri kartı · paketler");
});
