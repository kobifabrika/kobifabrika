import type { Locator, Page, TestInfo } from "@playwright/test";

import { PASSWORD, uniqueEmail, uniquePhone } from "./support/api";
import { linkFromEmail, subjects } from "./support/mailpit";
import { toast } from "./support/nav";
import { target } from "./support/target";
import { expect, isDesktop, test } from "./support/test";

const istanbulDay = (d: Date) => new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(d);

async function checkout(page: Page, info: TestInfo): Promise<Locator> {
  if (isDesktop(info)) return page.locator("aside").filter({ hasText: "Satışı tamamla" });
  await page.getByRole("button", { name: "Ödeme al" }).click();
  return page.getByRole("dialog");
}

/**
 * Sprint 8A "yeni esnaf" (SMS kapalı, pilotun gerçek hali): fiyat sayfasından kayıt (?paket=kalfa) → "E-postanı kontrol et" →
 * doğrulama (Mailpit) → kurulum sihirbazı (kuaför) → personel → hizmet → online randevuyu aç → müşteri kodsuz randevu talebi
 * (e-postalı; "alındı" e-postası) → Sahip onaylar (WhatsApp düğmesi, "onaylandı" e-postası) → randevu tamamlandı → satışa çevir
 * (nakit) → kasa bakiyesi → Bugün. Online randevu ileri bir saate alınır; "Tamamlandı" saat gelince işaretlenebildiği için
 * müşteri erken gelmiş gibi randevu panelden bugünün geçmiş bir saatine taşınır.
 */
test("yeni esnaf: kayıttan ilk satışa", async ({ page, newPublicPage, visit, check }, info) => {
  test.setTimeout(240_000);
  const email = uniqueEmail("esnaf");
  const businessName = `Yeni Salon ${Math.random().toString(36).slice(2, 6)}`;

  // 1-2. Kayıt (fiyat sayfasından Kalfa) ve e-posta doğrulama.
  await visit(page, "/kayit?paket=kalfa", "Yeni esnaf · kayıt");
  await expect(page.getByText(/Seçtiğiniz paket: Kalfa/)).toBeVisible();
  await page.getByLabel("Ad soyad").fill("Selin Usta");
  await page.getByLabel("E-posta").fill(email);
  await page.getByLabel("Cep telefonu").fill(uniquePhone());
  await page.getByLabel("Şifre", { exact: true }).fill(PASSWORD);
  await page.locator("#terms").check();
  await page.getByRole("button", { name: "Hesap oluştur" }).click();
  await expect(page.getByRole("heading", { name: "E-postanı kontrol et" })).toBeVisible();
  const verify = await linkFromEmail(email, /E-posta adresini doğrula/, "/e-posta-dogrula");
  await page.goto(verify);
  await expect(page.getByText("E-postan doğrulandı")).toBeVisible();

  // 3. Kurulum sihirbazı.
  await page.goto("/kurulum");
  await page.getByLabel("İşletme adı").fill(businessName);
  await page.getByRole("radio", { name: /Kuaför\/Berber/ }).click();
  await page.getByLabel("İşletme telefonu").fill("0212 555 44 33");
  await page.getByLabel("İl", { exact: true }).selectOption("İstanbul");
  await page.getByLabel("İlçe").selectOption("Kadıköy");
  await page.getByRole("button", { name: "Devam" }).click();
  await expect(page.getByRole("heading", { name: "Çalışma saatlerin" })).toBeVisible();
  await page.getByRole("button", { name: "Devam" }).click();
  await page.getByRole("button", { name: "Kurulumu bitir" }).click();
  await expect(page).toHaveURL(/\/panel$/);
  await check(page, "Yeni esnaf · ilk Bugün");

  // 4. Personel (her gün 00:00-23:59: test saatten bağımsız olsun).
  await visit(page, "/panel/personel/yeni", "Yeni esnaf · personel ekle");
  await page.getByLabel("Ad soyad").fill("Elif Usta");
  await page.getByRole("button", { name: "Personeli ekle" }).click();
  await expect(toast(page, /Personel eklendi/)).toBeVisible();
  await expect(page).toHaveURL(/\/panel\/personel\/[0-9a-f-]{36}/);
  const staffId = page.url().match(/personel\/([0-9a-f-]+)/)![1]!;
  const api = page.request;
  const apiBase = target().apiUrl; // tarayıcı bağlamının istekleri oturum çerezini taşır
  const week = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
  expect(
    (await api.put(`${apiBase}/api/business/working-hours`, { data: { days: week.map((day) => ({ day, isClosed: false, opensAt: "00:00", closesAt: "23:59" })) } })).ok(),
  ).toBe(true);
  expect(
    (await api.put(`${apiBase}/api/staff/${staffId}/working-hours`, { data: { days: week.map((day) => ({ day, ranges: [{ start: "00:00", end: "23:59" }] })) } })).ok(),
  ).toBe(true);

  // 5. Hizmet.
  await visit(page, "/panel/urunler/yeni", "Yeni esnaf · hizmet ekle");
  await page.getByRole("radio", { name: "Hizmet" }).click();
  await page.getByLabel("Ad", { exact: true }).fill("Saç kesimi");
  await page.getByLabel("Satış fiyatı (TL)").fill("400");
  await page.getByLabel("Süre (dakika)").fill("30");
  await page.getByRole("button", { name: "Hizmeti ekle" }).click();
  await expect(toast(page, /eklendi/)).toBeVisible();

  // 6. Online randevuyu aç, linki al.
  await visit(page, "/panel/ayarlar/randevu", "Yeni esnaf · online randevu ayarı");
  await page.getByRole("switch", { name: "Online randevu sayfası açık" }).click();
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, /Sayfanız açık/)).toBeVisible();
  const link = (await page.locator("code").filter({ hasText: "/r/" }).textContent())!.trim();
  const slug = link.split("/r/")[1]!;

  // 7. Müşteri: kodsuz, e-postalı talep.
  const customer = await newPublicPage();
  await visit(customer, `/r/${slug}`, "Yeni esnaf · müşteri online randevu");
  await customer.getByRole("button", { name: /Saç kesimi/ }).click();
  await customer.getByRole("button", { name: /Fark etmez/ }).click();
  await customer.getByRole("button", { name: /^\d{2}:\d{2}$/ }).first().click();
  const customerEmail = uniqueEmail("musteri");
  await customer.getByLabel("Ad", { exact: true }).fill("Deniz");
  await customer.getByLabel("Soyad", { exact: true }).fill("Ak");
  await customer.getByLabel("Cep telefonu").fill(uniquePhone());
  await customer.getByLabel("E-posta (isteğe bağlı)").fill(customerEmail);
  await customer.waitForTimeout(3_500); // bot tuzağı: 3 sn'den hızlı gönderim kayıt açmaz
  await customer.getByRole("button", { name: "Randevu talebini gönder" }).click();
  await expect(customer.getByText("Randevu talebiniz alındı.")).toBeVisible();
  await expect.poll(async () => (await subjects(customerEmail)).some((s) => /randevu talebiniz alındı/.test(s)), { timeout: 20_000 }).toBe(true);

  // 8. Sahip onaylar: WhatsApp düğmesi ve "onaylandı" e-postası.
  const calendar = (await (await api.get(`${apiBase}/api/calendar?from=${istanbulDay(new Date())}&to=${istanbulDay(new Date(Date.now() + 13 * 86_400_000))}`)).json()) as {
    appointments: { id: string; startAt: string; source: string }[];
  };
  const online = calendar.appointments.find((a) => a.source === "Online")!;
  await visit(page, `/panel/randevular?tarih=${istanbulDay(new Date(online.startAt))}&randevu=${online.id}`, "Yeni esnaf · onay bekleyen");
  const sheet = page.getByRole("dialog");
  await sheet.getByRole("button", { name: "Onayla" }).click();
  await expect(toast(page, "Randevu onaylandı.")).toBeVisible();
  await expect(sheet.getByRole("link", { name: "Onayı WhatsApp'tan bildir" })).toHaveAttribute("href", /^https:\/\/wa\.me\//);
  await expect.poll(async () => (await subjects(customerEmail)).some((s) => /randevunuz onaylandı/.test(s)), { timeout: 20_000 }).toBe(true);
  await page.keyboard.press("Escape");

  // 9. Müşteri erken geldi: randevu bugünün geçmiş bir saatine alınır, Tamamlandı.
  // Randevu gece yarısını aşamaz: 00:00-01:00 arası koşuda bugünün 00:00'ı (İstanbul UTC+3) kullanılır.
  const past = new Date(Date.now() - 60 * 60_000);
  past.setUTCMinutes(Math.floor(past.getUTCMinutes() / 5) * 5, 0, 0);
  const istanbulMidnight = new Date(`${istanbulDay(new Date())}T00:00:00+03:00`);
  if (past < istanbulMidnight) past.setTime(istanbulMidnight.getTime());
  const detail = (await (await api.get(`${apiBase}/api/appointments/${online.id}`)).json()) as { staffMemberId: string; productId: string };
  expect(
    (
      await api.put(`${apiBase}/api/appointments/${online.id}`, {
        data: {
          staffMemberId: detail.staffMemberId,
          productId: detail.productId,
          startAt: past.toISOString(),
          endAt: new Date(past.getTime() + 30 * 60_000).toISOString(),
          note: null,
          customerPackageId: null,
          notifyCustomer: false,
        },
      })
    ).ok(),
  ).toBe(true);
  await visit(page, `/panel/randevular?tarih=${istanbulDay(past)}&randevu=${online.id}`, "Yeni esnaf · tamamlanacak randevu");
  await sheet.getByRole("button", { name: "Tamamlandı" }).click();
  await expect(toast(page, "Randevu tamamlandı.")).toBeVisible();

  // 10. Satışa çevir, nakit.
  await sheet.getByRole("link", { name: "Satışa çevir" }).click();
  await expect(page.getByText(/Randevudan satış: Saç kesimi · Elif Usta/)).toBeVisible();
  const pay = await checkout(page, info);
  await pay.getByRole("radio", { name: "Nakit" }).click();
  await pay.getByRole("button", { name: /Satışı tamamla/ }).click();
  await expect(page.getByText(/SAT-\d{4}-\d{6} kaydedildi/)).toBeVisible();

  // 11. Kasa bakiyesi: Nakit Kasa 400 TL.
  await visit(page, "/panel/cari-kasa?sekme=kasa", "Yeni esnaf · kasa");
  await expect(page.getByRole("listitem").filter({ hasText: "Nakit Kasa" }).getByText("400,00 TL").first()).toBeVisible();

  // 12. Bugün: satış ve randevu.
  await visit(page, "/panel", "Yeni esnaf · Bugün");
  await expect(page.getByText("400,00 TL").first()).toBeVisible();
  await expect(page.getByRole("region", { name: /Yarının randevuları/ })).toBeVisible();
  await expect(page.getByText("Deniz Ak").first()).toBeVisible();
});
