import { expect as pwExpect } from "@playwright/test";

import { createContact, uniqueEmail, uniquePhone, type ApiUser } from "./support/api";
import { at, book, createService, createStaff, openAllDay, workAllDay } from "./support/appointments";
import { linkFromEmail, subjects } from "./support/mailpit";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

test.use({ ownerSector: "HairBarber" });

const istanbulDay = (d: Date) => new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(d);

async function openBooking(owner: ApiUser) {
  await openAllDay(owner);
  const service = await createService(owner, "Saç kesimi", 30, 350);
  const ayse = await createStaff(owner, "Ayşe Usta");
  await workAllDay(owner, ayse.id);
  const settings = await owner.put<{ slug: string }>("/api/business/booking-settings", {
    enabled: true,
    autoConfirm: true, // SMS kapalıyken yok sayılır: talep her zaman "Beklemede" düşer.
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
 * Sprint 8A, SMS'siz pilot: online randevu kodsuz (adım çubuğunda "Kod" yok, e-posta isteğe bağlı) → "Talebiniz alındı" ekranı
 * ve e-postası → panelde Onayla → "Müşteriye haber verin" WhatsApp linki (hazır metin) + "Onaylandı" e-postası; Bugün'de
 * "Yarının randevuları" → WhatsApp → "Hatırlatıldı"; Ayarlar → Randevu'da otomatik onay kapalı ve açıklamalı.
 */
test("online randevu kodsuz, WhatsApp bildirimi ve yarının randevuları", async ({
  owner,
  ownerPage,
  newPublicPage,
  visit,
  check,
}) => {
  const { slug, name, service, ayse } = await openBooking(owner);

  // --- Müşteri: kodsuz talep, e-postalı. ---
  const page = await newPublicPage();
  await visit(page, `/r/${slug}`, "Online randevu (kodsuz) · hizmet");
  await expect(page.getByText("Adım 1/4")).toBeVisible();
  await page.getByRole("button", { name: /Saç kesimi/ }).click();
  await page.getByRole("button", { name: /Fark etmez/ }).click();
  await page
    .getByRole("button", { name: /^\d{2}:\d{2}$/ })
    .first()
    .click();
  await expect(page.getByRole("heading", { name: "Bilgileriniz" })).toBeVisible();
  await expect(page.getByText("Adım 4/4")).toBeVisible();
  const phone = uniquePhone();
  const email = uniqueEmail("musteri");
  await page.getByLabel("Ad", { exact: true }).fill("Deniz");
  await page.getByLabel("Soyad", { exact: true }).fill("Ak");
  await page.getByLabel("Cep telefonu").fill(phone);
  await expect(page.getByText("Kampanya ve fırsatlardan e-posta ile haberdar olmak istiyorum.")).toHaveCount(0);
  await page.getByLabel("E-posta (isteğe bağlı)").fill(email);
  await expect(page.getByText("Kampanya ve fırsatlardan e-posta ile haberdar olmak istiyorum.")).toBeVisible();
  // Bot tuzağı alanı görünmez ve klavyeyle odaklanmaz.
  await expect(page.locator('input[name="website"]')).toHaveAttribute("tabindex", "-1");
  await check(page, "Online randevu (kodsuz) · bilgiler");
  // Bot tuzağı: sayfa açıldıktan 3 saniyeden kısa sürede gelen talep kayıt açmaz (sahte başarı). Test bir insandan hızlı.
  await page.waitForTimeout(3_500);
  await page.getByRole("button", { name: "Randevu talebini gönder" }).click();
  await expect(page.getByRole("heading", { name: "Randevunuz alındı" })).toBeVisible();
  await expect(page.getByText("Randevu talebiniz alındı.")).toBeVisible();
  await expect(page.getByText(/İşletme onayladığında e-postayla bilgi vereceğiz/)).toBeVisible();
  await check(page, "Online randevu (kodsuz) · alındı");
  await linkFromEmail(email, /randevu talebiniz alındı/, `/r/${slug}/randevu/`);

  // --- Panel: Beklemede (otomatik onay açık olsa bile) → Onayla → WhatsApp + e-posta. ---
  const calendar = await owner.get<{ appointments: { id: string; startAt: string; status: string; source: string }[] }>(
    `/api/calendar?from=${istanbulDay(new Date())}&to=${istanbulDay(new Date(Date.now() + 13 * 86_400_000))}`,
  );
  const online = calendar.appointments.find((a) => a.source === "Online")!;
  expect(online.status).toBe("Pending");
  const day = istanbulDay(new Date(online.startAt));
  await visit(ownerPage, `/panel/randevular?tarih=${day}&randevu=${online.id}`, "Randevu ayrıntısı · kodsuz online");
  const sheet = ownerPage.getByRole("dialog");
  await expect(sheet.getByText(/müşteriye WhatsApp'tan haber verebilirsiniz/)).toBeVisible();
  await sheet.getByRole("button", { name: "Onayla" }).click();
  await expect(toast(ownerPage, "Randevu onaylandı.")).toBeVisible();
  await expect(sheet.getByText("Müşteriye haber verin")).toBeVisible();
  const notify = sheet.getByRole("link", { name: "Onayı WhatsApp'tan bildir" });
  await expect(notify).toHaveAttribute("href", /^https:\/\/wa\.me\/90\d{10}\?text=/);
  const href = decodeURIComponent((await notify.getAttribute("href"))!);
  expect(href).toContain(name);
  expect(href).toContain(`/r/${slug}/randevu/`);
  await check(ownerPage, "Randevu ayrıntısı · WhatsApp'tan bildir");
  await pwExpect
    .poll(async () => (await subjects(email)).some((s) => /randevunuz onaylandı/.test(s)), { timeout: 20_000 })
    .toBe(true);

  // --- Bugün → Yarının randevuları → WhatsApp → Hatırlatıldı. ---
  const yarin = istanbulDay(new Date(Date.now() + 86_400_000));
  const contact = await createContact(owner, { firstName: "Ece", lastName: "Su" });
  await book(owner, { contactId: contact.id, staffId: ayse.id, serviceId: service.id, startAt: at(yarin, "11:00") });
  await visit(ownerPage, "/panel", "Bugün · yarının randevuları");
  const card = ownerPage.getByRole("region", { name: /Yarının randevuları/ });
  await expect(card.getByText("Ece Su")).toBeVisible();
  const remind = card.getByRole("listitem").filter({ hasText: "Ece Su" }).getByRole("link", { name: "WhatsApp" });
  await expect(remind).toHaveAttribute("href", /^https:\/\/wa\.me\//);
  // Yeni sekme (WhatsApp) testte açılmasın: tıklama kaydı aynı şekilde gider.
  await ownerPage.context().route("https://wa.me/**", (route) => route.fulfill({ status: 200, body: "ok" }));
  const popup = ownerPage.waitForEvent("popup");
  await remind.click();
  await (await popup).close();
  await expect(card.getByText(/Hatırlatıldı \d{2}:\d{2}/)).toBeVisible();
  await check(ownerPage, "Bugün · hatırlatıldı");

  // --- Ayarlar → Randevu: otomatik onay kapalı, açıklamalı; SMS sekmesi yok. ---
  await visit(ownerPage, "/panel/ayarlar/randevu", "Ayarlar · randevu (SMS kapalı)");
  await expect(ownerPage.getByRole("switch", { name: "Otomatik onay" })).toBeDisabled();
  await expect(ownerPage.getByText(/Pilot döneminde telefon SMS koduyla doğrulanmadığı için/)).toBeVisible();
  await expect(ownerPage.getByRole("link", { name: "SMS", exact: true })).toHaveCount(0);
});

/**
 * SMS kapalıyken Kampanyalar: boş durum SMS demez; otomasyon anahtarları açıklamayla kilitli (listede ve düzenlemede),
 * metin yine düzenlenip kaydedilebilir.
 */
test("SMS kapalıyken kampanya boş durumu ve kilitli otomasyonlar", async ({ ownerPage: page, visit }) => {
  await visit(page, "/panel/kampanyalar", "SMS kapalı · Kampanyalar (boş)");
  await expect(page.getByText("Henüz kampanya yok")).toBeVisible();
  await expect(page.getByText(/E-posta ve WhatsApp kampanyaları yakında/)).toBeVisible();
  await expect(page.getByText(/izinli müşterilerinize SMS gönderin/)).toHaveCount(0);

  await visit(page, "/panel/kampanyalar?sekme=otomasyonlar", "SMS kapalı · Otomasyonlar");
  await expect(page.getByText(/SMS pilot döneminde kapalı olduğu için şimdilik açılamaz/)).toBeVisible();
  const list = page.getByRole("list", { name: "Otomasyonlar" });
  await expect(list.getByRole("switch", { name: "Memnuniyet anketi açık" })).toBeDisabled();
  await expect(list.getByRole("switch", { name: "Doğum günü açık" })).toBeDisabled();

  await page.getByRole("link", { name: "Randevu sonrası teşekkür: düzenle" }).click();
  await expect(page.getByRole("heading", { name: "Randevu sonrası teşekkür" })).toBeVisible();
  await expect(page.getByRole("switch", { name: "Otomasyon açık" })).toBeDisabled();
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Otomasyon kaydedildi.")).toBeVisible();
});
