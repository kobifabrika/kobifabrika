import { expect as pwExpect, request, type Page } from "@playwright/test";

import { apiUrl } from "./scripts/env.mjs";
import { createContact, uniquePhone, type ApiUser } from "./support/api";
import { at, book, createService, createStaff, openAllDay, workAllDay } from "./support/appointments";
import { smsCode, smsTexts } from "./support/mailpit";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

test.use({ ownerSector: "HairBarber" });

type Slots = { days: { date: string; times: string[] }[] };

const longDay = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", weekday: "long", day: "numeric", month: "long" });

/** Online randevuyu API ile açar (4 saat sonrasından itibaren, 30 dk aralık, en az 2 saat kala iptal). */
async function openBooking(owner: ApiUser) {
  await openAllDay(owner);
  const service = await createService(owner, "Saç kesimi", 30, 350);
  const ayse = await createStaff(owner, "Ayşe Usta");
  await workAllDay(owner, ayse.id);
  const settings = await owner.put<{ slug: string }>("/api/business/booking-settings", {
    enabled: true,
    autoConfirm: false,
    minNoticeMinutes: 240,
    maxDaysAhead: 30,
    slotMinutes: 30,
    reminderHoursBefore: 24,
    customerCancelHours: 2,
  });
  const business = await owner.get<{ name: string }>("/api/business");
  return { slug: settings.slug, service, ayse, name: business.name };
}

/**
 * Herkese açık API'den personelin, en az minFree boş saati olan ilk gündeki ilk boş saati. "Dolu saat" adımında aynı günde başka
 * saat kalmalı: akşam koşunca (en erken bildirim 4 saat) bugün tek boş saat kalabiliyordu (CI 2026-10-15, 18:00 sonrası).
 */
async function firstSlot(slug: string, serviceId: string, staffId: string, minFree = 1) {
  const ctx = await request.newContext({ baseURL: apiUrl });
  try {
    const res = await ctx.get(`/api/public/booking/${slug}/slots?serviceId=${serviceId}&staffId=${staffId}&days=7`);
    const slots = (await res.json()) as Slots;
    const day = slots.days.find((d) => d.times.length >= minFree)!;
    return { day: day.date, time: day.times[0]! };
  } finally {
    await ctx.dispose();
  }
}

async function fillPerson(page: Page, phone: string, first = "Deniz", last = "Ak") {
  await page.getByLabel("Ad", { exact: true }).fill(first);
  await page.getByLabel("Soyad", { exact: true }).fill(last);
  await page.getByLabel("Cep telefonu").fill(phone);
}

/**
 * Sprint 5B online randevu, telefonda baştan sona: hizmet → Fark etmez → saat → bilgiler → SMS kodu (Mailpit yansıması) → onay;
 * yanlış kod; panelde Beklemede → Onayla (müşteriye onay SMS'i); dolu saat (başka müşteri kod girerken saat doldu → saat adımına
 * döner, aynı kodla başka saat); müşterinin iptal linki.
 */
test("online randevu: kodla randevu, onay, dolu saat ve iptal linki", async ({ owner, ownerPage, newPublicPage, check, visit, guard }) => {
  guard.allow(400, /\/api\/public\/booking\/.+\/appointments$/); // yanlış kod (beklenen)
  guard.allow(409, /\/api\/public\/booking\/.+\/appointments$/); // dolu saat (beklenen)
  const { slug, service, ayse, name } = await openBooking(owner);

  // --- Müşteri A: Fark etmez, yanlış kod, doğru kod. ---
  const page = await newPublicPage();
  await visit(page, `/r/${slug}`, "Online randevu · hizmet");
  await expect(page).toHaveTitle(`${name} · Online randevu`);
  await expect(page.getByText(name).first()).toBeVisible();
  await page.getByRole("button", { name: /Saç kesimi/ }).click();
  await expect(page.getByRole("heading", { name: "Personel seçin" })).toBeVisible();
  await check(page, "Online randevu · personel");
  await page.getByRole("button", { name: /Fark etmez/ }).click();
  await expect(page.getByRole("heading", { name: "Gün ve saat seçin" })).toBeVisible();
  await check(page, "Online randevu · saat");
  const firstTime = page.getByRole("button", { name: /^\d{2}:\d{2}$/ }).first();
  const chosen = (await firstTime.textContent())!.trim();
  await firstTime.click();
  await expect(page.getByRole("heading", { name: "Bilgileriniz" })).toBeVisible();
  await expect(page.getByRole("link", { name: "KVKK aydınlatma metni" })).toHaveAttribute("href", "/kvkk");
  const phoneA = uniquePhone();
  await fillPerson(page, phoneA);
  await check(page, "Online randevu · bilgiler");
  await page.getByRole("button", { name: "Kod gönder" }).click();
  await expect(page.getByRole("heading", { name: "Telefon doğrulama" })).toBeVisible();
  const code = await smsCode(phoneA);
  expect((await smsTexts(phoneA))[0]).toContain(`${name.slice(0, 10)}`);
  await check(page, "Online randevu · kod");

  await page.getByLabel("Doğrulama kodu").fill(code === "000000" ? "111111" : "000000");
  await page.getByRole("button", { name: "Randevuyu onayla" }).click();
  await expect(page.getByText("Kod hatalı. 2 deneme hakkınız kaldı.")).toBeVisible();
  await page.getByLabel("Doğrulama kodu").fill(code);
  await page.getByRole("button", { name: "Randevuyu onayla" }).click();
  await expect(page.getByRole("heading", { name: "Randevunuz alındı" })).toBeVisible();
  await expect(page.getByText("Randevu talebiniz alındı.")).toBeVisible();
  await expect(page.getByText("İşletme onayladığında SMS ile bilgi vereceğiz.")).toBeVisible();
  await expect(page.getByText(new RegExp(`· ${chosen}-`))).toBeVisible();
  await check(page, "Online randevu · onay");
  await pwExpect.poll(async () => (await smsTexts(phoneA)).some((t) => t.includes("randevu talebiniz alındı"))).toBe(true);

  // Müşterinin randevu sayfası: Beklemede.
  await page.getByRole("link", { name: "Randevuyu görüntüle veya iptal et" }).click();
  await expect(page.getByRole("heading", { name: "Randevunuz" })).toBeVisible();
  await expect(page.getByText("Beklemede")).toBeVisible();
  await check(page, "Müşterinin randevu sayfası");
  const manageUrl = page.url();

  // --- Panel: Onay bekleyen → Onayla. ---
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(new Date());
  const calendar = await owner.get<{ appointments: { id: string; startAt: string; status: string; source: string }[] }>(
    `/api/calendar?from=${today}&to=${new Date(Date.parse(`${today}T00:00:00Z`) + 13 * 86_400_000).toISOString().slice(0, 10)}`,
  );
  const online = calendar.appointments.find((a) => a.source === "Online")!;
  expect(online.status).toBe("Pending");
  const day = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(new Date(online.startAt));
  await visit(ownerPage, `/panel/randevular?tarih=${day}`, "Takvim · onay bekleyen");
  await expect(ownerPage.getByText("Onay bekleyen 1")).toBeVisible();
  await visit(ownerPage, `/panel/randevular?tarih=${day}&randevu=${online.id}`, "Randevu ayrıntısı · beklemede");
  const sheet = ownerPage.getByRole("dialog");
  await expect(sheet.getByText("Online randevu")).toBeVisible();
  await expect(sheet.getByRole("button", { name: "Reddet" })).toBeVisible();
  await sheet.getByRole("button", { name: "Onayla" }).click();
  await expect(toast(ownerPage, "Randevu onaylandı.")).toBeVisible();
  await pwExpect.poll(async () => (await smsTexts(phoneA)).some((t) => t.includes("randevunuz onaylandı"))).toBe(true);

  // --- Müşteri B: Ayşe Usta; kod girerken saat doluyor. ---
  const pageB = await newPublicPage();
  await visit(pageB, `/r/${slug}`, "Online randevu · ikinci müşteri");
  await pageB.getByRole("button", { name: /Saç kesimi/ }).click();
  await pageB.getByRole("button", { name: /Ayşe Usta/ }).click();
  const slot = await firstSlot(slug, service.id, ayse.id, 2);
  if (slot.day !== (await firstSlot(slug, service.id, ayse.id)).day) {
    await pageB.getByRole("radio", { name: new RegExp(`^${longDay.format(new Date(`${slot.day}T00:00:00Z`))}`) }).click();
  }
  await pageB.getByRole("button", { name: slot.time, exact: true }).first().click();
  const phoneB = uniquePhone();
  await fillPerson(pageB, phoneB, "Ece", "Su");
  await pageB.getByRole("button", { name: "Kod gönder" }).click();
  const codeB = await smsCode(phoneB);
  const walkIn = await createContact(owner, { firstName: "Kapıdan", lastName: "Gelen" });
  await book(owner, { contactId: walkIn.id, staffId: ayse.id, serviceId: service.id, startAt: at(slot.day, slot.time) });
  await pageB.getByLabel("Doğrulama kodu").fill(codeB);
  await pageB.getByRole("button", { name: "Randevuyu onayla" }).click();
  await expect(toast(pageB, "Bu saat az önce doldu. Başka bir saat seçin.")).toBeVisible();
  await expect(pageB.getByRole("heading", { name: "Gün ve saat seçin" })).toBeVisible();
  await expect(pageB.getByRole("button", { name: slot.time, exact: true })).toHaveCount(0);
  // Aynı (doğrulanmış) kodla başka saat: kod adımına döner, kod tekrar girilir.
  await pageB.getByRole("button", { name: /^\d{2}:\d{2}$/ }).first().click();
  await expect(pageB.getByRole("heading", { name: "Telefon doğrulama" })).toBeVisible();
  await pageB.getByLabel("Doğrulama kodu").fill(codeB);
  await pageB.getByRole("button", { name: "Randevuyu onayla" }).click();
  await expect(pageB.getByText("Randevu talebiniz alındı.")).toBeVisible();

  // --- Müşteri A iptal linkinden iptal eder; işletmeye bildirim düşer. ---
  await visit(page, manageUrl, "Müşterinin randevu sayfası · onaylı");
  await expect(page.getByText("Onaylı")).toBeVisible();
  await page.getByRole("button", { name: "Randevuyu iptal et" }).click();
  await page.getByRole("button", { name: "Evet, iptal et" }).click();
  await expect(page.getByText("Randevunuz iptal edildi.")).toBeVisible();
  await check(page, "Müşterinin randevu sayfası · iptal");
  const notifications = await owner.get<{ items: { type: string }[] }>("/api/notifications");
  expect(notifications.items.map((n) => n.type)).toContain("appointment.customer_cancelled");
});
