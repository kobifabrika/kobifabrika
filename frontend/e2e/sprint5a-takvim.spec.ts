import type { Locator, Page } from "@playwright/test";

import { createContact } from "./support/api";
import { at, book, createService, createStaff, openAllDay, type Appointment } from "./support/appointments";
import { istanbulDay } from "./support/finance";
import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

/** Takvim ızgarası: 1 dakika = 1,4 px (components/appointments/time-grid.tsx). */
const PX_PER_MIN = 1.4;

async function drag(page: Page, target: Locator, dy: number, fromBottom = false) {
  await target.scrollIntoViewIfNeeded();
  const box = (await target.boundingBox())!;
  const x = box.x + box.width / 2;
  const y = fromBottom ? box.y + box.height - 3 : box.y + 8;
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x, y + Math.sign(dy) * 10, { steps: 2 });
  await page.mouse.move(x, y + dy, { steps: 12 });
  await page.mouse.up();
}

/**
 * Sprint 5A takvim: gün görünümü (masaüstünde personel kolonlu, telefonda personel seçicili), yeni randevu (mevcut ve hızlı yeni
 * müşteri), masaüstünde sürükle-bırak ile taşıma, süre uzatma ve çakışmada geri dönme; telefonda "Taşı" ve gün okları; hafta görünümü.
 */
test("takvim: yeni randevu, sürükle-bırak / Taşı, çakışma ve hafta görünümü", async ({ owner, ownerPage: page, visit, check, guard }, info) => {
  await openAllDay(owner);
  const service = await createService(owner, "Saç kesimi", 45);
  const ayse = await createStaff(owner, "Ayşe Usta");
  const mert = await createStaff(owner, "Mert Usta");
  await createContact(owner, { firstName: "Zeynep", lastName: "Kaya" });
  const day = istanbulDay(1);

  await visit(page, `/panel/randevular?tarih=${day}`, "Takvim · gün");
  if (isDesktop(info)) {
    const grid = page.getByRole("region", { name: "Takvim ızgarası" });
    await expect(grid.getByText("Ayşe Usta")).toBeVisible();
    await expect(grid.getByText("Mert Usta")).toBeVisible();
  } else {
    await expect(page.getByRole("radio", { name: "Ayşe Usta" })).toHaveAttribute("aria-checked", "true");
  }

  // Yeni randevu: mevcut müşteri, Ayşe 10:00 (bitiş hizmet süresinden).
  await page.getByRole("button", { name: "Yeni randevu" }).click();
  let form = page.getByRole("dialog");
  await form.getByPlaceholder("Ad veya telefon ara").fill("Zeynep");
  await form.getByRole("button", { name: /Zeynep Kaya/ }).click();
  await form.getByLabel("Personel").selectOption({ label: "Ayşe Usta" });
  await form.getByLabel("Hizmet").selectOption({ label: "Saç kesimi (45 dk)" });
  await form.getByLabel("Tarih").fill(day);
  await form.getByLabel("Başlangıç").fill("10:00");
  await expect(form.getByLabel("Bitiş")).toHaveValue("10:45");
  await check(page, "Takvim · yeni randevu");
  await form.getByRole("button", { name: "Randevu ver" }).click();
  await expect(toast(page, "Randevu verildi.")).toBeVisible();
  const zeynep = page.getByRole("button", { name: /^10:00-10:45 Zeynep Kaya/ });
  await expect(zeynep).toBeVisible();

  // Hızlı yeni müşteri: Mert 10:00 (farklı personel, çakışmaz).
  await page.getByRole("button", { name: "Yeni randevu" }).click();
  form = page.getByRole("dialog");
  await form.getByRole("button", { name: "Yeni müşteri" }).click();
  await form.getByLabel("Ad soyad").fill("Can Demir");
  await form.getByRole("button", { name: "Ekle ve seç" }).click();
  await form.getByLabel("Personel").selectOption({ label: "Mert Usta" });
  await form.getByLabel("Hizmet").selectOption({ label: "Saç kesimi (45 dk)" });
  await form.getByLabel("Tarih").fill(day);
  await form.getByLabel("Başlangıç").fill("10:00");
  await form.getByRole("button", { name: "Randevu ver" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);

  const list = async () => (await owner.get<{ appointments: (Appointment & { contactName: string })[] }>(`/api/calendar?from=${day}&to=${day}`)).appointments;
  const of = async (name: string) => (await list()).find((a) => a.contactName === name)!;
  await expect.poll(async () => (await of("Can Demir"))?.staffMemberId).toBe(mert.id);

  if (isDesktop(info)) {
    // Sürükle: 2 saat aşağı → 12:00-12:45.
    await drag(page, zeynep, 120 * PX_PER_MIN);
    await expect(toast(page, "Randevu taşındı.")).toBeVisible();
    await expect.poll(async () => (await of("Zeynep Kaya")).startAt).toBe(at(day, "12:00"));

    // Alt kenardan çek: 30 dk uzat → 12:00-13:15.
    const moved = page.getByRole("button", { name: /^12:00-12:45 Zeynep Kaya/ });
    await drag(page, moved, 30 * PX_PER_MIN, true);
    await expect.poll(async () => { const a = await of("Zeynep Kaya"); return `${a.startAt} ${a.endAt}`; }).toBe(`${at(day, "12:00")} ${at(day, "13:15")}`);

    // Çakışma: Can Demir'i Ayşe'nin kolonuna 12:00'ye sürükle → sunucu reddeder (409), eski yerine döner.
    guard.allow(409, /\/api\/appointments\//);
    const can = page.getByRole("button", { name: /^10:00-10:45 Can Demir/ });
    await can.scrollIntoViewIfNeeded();
    const ayseColumn = page.getByTestId(`kolon-${ayse.id}_${day}`);
    const from = (await can.boundingBox())!;
    const to = (await ayseColumn.boundingBox())!;
    await page.mouse.move(from.x + from.width / 2, from.y + 8);
    await page.mouse.down();
    await page.mouse.move(from.x + from.width / 2 + 10, from.y + 18, { steps: 2 });
    await page.mouse.move(to.x + to.width / 2, from.y + 8 + 120 * PX_PER_MIN, { steps: 12 });
    await page.mouse.up();
    await expect(toast(page, /arasında başka bir randevuda/)).toBeVisible();
    await expect(page.getByRole("button", { name: /^10:00-10:45 Can Demir/ })).toBeVisible();
    expect((await of("Can Demir")).staffMemberId).toBe(mert.id);
    await check(page, "Takvim · sürükle-bırak sonrası");
  } else {
    // Telefon: randevuya dokun → Taşı → 12:00 (süre korunur).
    await zeynep.click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByText("Saç kesimi · Ayşe Usta")).toBeVisible();
    await check(page, "Takvim · randevu ayrıntısı");
    await sheet.getByRole("button", { name: "Taşı" }).click();
    form = page.getByRole("dialog");
    await form.getByLabel("Başlangıç").fill("12:00");
    await expect(form.getByLabel("Bitiş")).toHaveValue("12:45");
    await form.getByRole("button", { name: "Kaydet" }).click();
    await expect(toast(page, "Randevu güncellendi.")).toBeVisible();
    await expect.poll(async () => (await of("Zeynep Kaya")).startAt).toBe(at(day, "12:00"));
    await expect(page.getByRole("button", { name: /^12:00-12:45 Zeynep Kaya/ })).toBeVisible();

    // Personel seçici ve gün okları.
    await page.getByRole("radio", { name: "Mert Usta" }).click();
    await expect(page.getByRole("button", { name: /^10:00-10:45 Can Demir/ })).toBeVisible();
    await page.getByRole("button", { name: "Sonraki gün" }).click();
    await expect(page).toHaveURL(new RegExp(`tarih=${istanbulDay(2)}`));
    await page.getByRole("button", { name: "Önceki gün" }).click();
    await expect(page).toHaveURL(new RegExp(`tarih=${day}`));
  }

  // Hafta görünümü için Mert'e bir randevu daha.
  const zeynepId = (await owner.get<{ items: { id: string }[] }>("/api/contacts?search=Zeynep")).items[0]!.id;
  await book(owner, { contactId: zeynepId, staffId: mert.id, serviceId: service.id, startAt: at(day, "15:00") });
  await page.reload();

  // Hafta görünümü.
  await page.getByRole("button", { name: "Hafta", exact: true }).click();
  await expect(page).toHaveURL(/gorunum=hafta/);
  if (isDesktop(info)) {
    await expect(page.getByRole("region", { name: "Takvim ızgarası" })).toBeVisible();
  } else {
    await expect(page.getByRole("button", { name: /Can Demir/ }).first()).toBeVisible();
  }
  await check(page, "Takvim · hafta");
});
