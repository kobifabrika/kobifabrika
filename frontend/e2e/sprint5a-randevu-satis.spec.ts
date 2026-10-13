import type { Locator, Page, TestInfo } from "@playwright/test";

import { addMember, createContact } from "./support/api";
import { book, createService, createStaff, openAllDay, pastSlot } from "./support/appointments";
import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

/** Ödeme paneli: masaüstünde sağdaki sütun, telefonda "Ödeme al" ile açılan alt sayfa. */
async function checkout(page: Page, info: TestInfo): Promise<Locator> {
  if (isDesktop(info)) return page.locator("aside").filter({ hasText: "Satışı tamamla" });
  await page.getByRole("button", { name: "Ödeme al" }).click();
  return page.getByRole("dialog");
}

/**
 * Sprint 5A: Tamamlandı → Satışa çevir (hızlı satış müşteri, hizmet ve personelle dolu) → satış randevuya bağlanır, ikinci kez
 * çevrilemez; Gelmedi ×2 → müşteri kartında ve yeni randevuda uyarı; Bugün kartı (Sahip hepsini, Personel kendisininkini görür).
 */
test("randevu: tamamla, satışa çevir, gelmedi uyarısı ve Bugün kartı", async ({ owner, ownerPage: page, visit, check, pageAs }, info) => {
  await openAllDay(owner);
  const service = await createService(owner, "Saç kesimi", 30, 350);
  const staffUser = await addMember(owner, "Staff", "Ece Personel");
  const me = await staffUser.get<{ user: { id: string } }>("/api/auth/me");
  const ece = await createStaff(owner, "Ece Personel", me.user.id);
  const mert = await createStaff(owner, "Mert Usta");
  const contact = await createContact(owner, { firstName: "Ayşe", lastName: "Yılmaz" });

  const slot = pastSlot(120);
  const appointment = await book(owner, { contactId: contact.id, staffId: mert.id, serviceId: service.id, startAt: slot.iso });

  // Ayrıntı → Tamamlandı → Satışa çevir.
  await visit(page, `/panel/randevular?tarih=${slot.day}&randevu=${appointment.id}`, "Randevu ayrıntısı");
  const sheet = page.getByRole("dialog");
  await sheet.getByRole("button", { name: "Tamamlandı" }).click();
  await expect(toast(page, "Randevu tamamlandı.")).toBeVisible();
  await sheet.getByRole("link", { name: "Satışa çevir" }).click();
  await expect(page).toHaveURL(new RegExp(`/panel/satislar/yeni\\?randevu=${appointment.id}`));
  await expect(page.getByText("Randevudan satış: Saç kesimi · Mert Usta")).toBeVisible();
  await check(page, "Hızlı satış · randevudan");
  const pay = await checkout(page, info);
  await expect(pay.getByText("Ayşe Yılmaz")).toBeVisible();
  await pay.getByRole("radio", { name: "Nakit" }).click();
  await pay.getByRole("button", { name: /Satışı tamamla/ }).click();
  await expect(page.getByText(/SAT-\d{4}-\d{6} kaydedildi/)).toBeVisible();

  const converted = await owner.get<{ saleId: string | null; saleNo: string | null; status: string }>(`/api/appointments/${appointment.id}`);
  expect(converted.saleId).not.toBeNull();
  const sale = await owner.get<{ appointmentId: string; staffName: string; total: number }>(`/api/sales/${converted.saleId}`);
  expect(sale.appointmentId).toBe(appointment.id);
  expect(sale.staffName).toBe("Mert Usta");
  expect(sale.total).toBe(350);

  // İkinci kez çevrilemez: ayrıntıda satış numarası var, "Satışa çevir" ve "Geri al" yok.
  await visit(page, `/panel/randevular?tarih=${slot.day}&randevu=${appointment.id}`, "Randevu ayrıntısı · satışa çevrildi");
  await expect(page.getByRole("dialog").getByRole("link", { name: converted.saleNo! })).toBeVisible();
  await expect(page.getByRole("dialog").getByRole("link", { name: "Satışa çevir" })).toHaveCount(0);
  await expect(page.getByRole("dialog").getByRole("button", { name: "Geri al" })).toHaveCount(0);

  // Gelmedi ×2 → müşteri kartında uyarı.
  for (const minutes of [300, 360]) {
    const s = pastSlot(minutes);
    const a = await book(owner, { contactId: contact.id, staffId: mert.id, serviceId: service.id, startAt: s.iso });
    await owner.post(`/api/appointments/${a.id}/no-show`);
  }
  await visit(page, `/panel/musteriler/${contact.id}`, "Müşteri kartı · gelmeyen müşteri uyarısı");
  await expect(page.getByText("Ayşe Yılmaz son 3 randevusundan 2'sine gelmedi.")).toBeVisible();
  await page.getByRole("link", { name: "Randevular" }).last().click();
  await expect(page.getByText("Gelmedi").first()).toBeVisible();
  await check(page, "Müşteri kartı · randevular");

  // Müşteri kartından "Randevu ver": müşteri seçili gelir, uyarı formda da görünür.
  await page.getByRole("link", { name: "Randevu ver" }).click();
  const form = page.getByRole("dialog");
  await expect(form.getByText("Ayşe Yılmaz", { exact: true })).toBeVisible();
  await form.getByLabel("Hizmet").selectOption({ label: "Saç kesimi (30 dk)" });
  await expect(form.getByText(/son 3 randevusundan 2'sine gelmedi/)).toBeVisible();
  await check(page, "Yeni randevu · gelmeyen müşteri uyarısı");
  await form.getByRole("button", { name: "Vazgeç" }).click();

  // Bugün kartı: Sahip hepsini, Personel (Ece) sadece kendisininkini görür.
  const today = pastSlot(0);
  await book(owner, { contactId: contact.id, staffId: ece.id, serviceId: service.id, startAt: pastSlot(30).iso });
  await visit(page, "/panel", "Bugün · randevular");
  if (today.day === slot.day) await expect(page.getByText("Bugünün randevuları")).toBeVisible();
  const staffPage = await pageAs(staffUser);
  await staffPage.goto("/panel");
  await check(staffPage, "Bugün · Personel randevuları");
  await expect(staffPage.getByText("Bugünkü randevularım")).toBeVisible();
  await expect(staffPage.getByText("Mert Usta")).toHaveCount(0);
});
