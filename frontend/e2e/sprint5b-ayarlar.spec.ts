import { createService, createStaff } from "./support/appointments";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

test.use({ ownerSector: "HairBarber" });

/**
 * Sprint 5B: Ayarlar → Randevu → Online randevu. Sayfa varsayılan kapalı (link "bulunamadı" der); personel yokken açılamaz;
 * personel eklenince açılır, otomatik onay, saat aralığı, en fazla gün ve hatırlatma kaydedilir; link önizlenir.
 */
test("online randevu ayarları: kapalı sayfa, personel şartı, kaydet @sms", async ({ owner, ownerPage: page, newPublicPage, visit, check, guard }) => {
  guard.allow(404, /\/api\/public\/booking\//); // kapalı sayfa (beklenen)
  guard.allow(409, /\/api\/business\/booking-settings$/); // personel yokken açma (beklenen)
  const { slug } = await owner.get<{ slug: string }>("/api/business/booking-settings");

  await visit(page, "/panel/ayarlar/randevu", "Ayarlar · Randevu · online kapalı");
  const enabled = page.getByRole("switch", { name: "Online randevu sayfası açık" });
  await expect(enabled).not.toBeChecked();
  await expect(page.getByText(`/r/${slug}`)).toBeVisible();

  const visitor = await newPublicPage();
  await visit(visitor, `/r/${slug}`, "Online randevu · kapalı");
  await expect(visitor.getByRole("heading", { name: "Online randevu sayfası bulunamadı" })).toBeVisible();

  // Personel yokken açılamaz.
  await enabled.click();
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(page.getByText(/en az bir aktif personelde "Online randevuda görünsün" açık olmalı/)).toBeVisible();

  // Hizmet ve personel eklenince açılır.
  await createService(owner, "Saç kesimi", 45, 300);
  await createStaff(owner, "Ayşe Usta");
  await visit(page, "/panel/ayarlar/randevu", "Ayarlar · Randevu");
  await expect(page.getByText(/Online sayfada 1 personel, 1 hizmet görünür/)).toBeVisible();
  await page.getByRole("switch", { name: "Online randevu sayfası açık" }).click();
  await page.getByRole("switch", { name: "Otomatik onay" }).click();
  await page.getByLabel("Saat aralığı").selectOption({ label: "30 dakika" });
  await page.getByLabel("En fazla kaç gün ilerisi").fill("60");
  await page.getByLabel("Hatırlatma SMS'i").selectOption({ label: "2 saat önce" });
  await page.getByLabel("Müşterinin online iptali").selectOption({ label: "En az 24 saat kala" });
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Online randevu ayarları kaydedildi. Sayfanız açık.")).toBeVisible();
  await check(page, "Ayarlar · Randevu · online açık");

  const saved = await owner.get<Record<string, unknown>>("/api/business/booking-settings");
  expect(saved).toMatchObject({
    enabled: true,
    autoConfirm: true,
    slotMinutes: 30,
    maxDaysAhead: 60,
    reminderHoursBefore: 2,
    customerCancelHours: 24,
    minNoticeMinutes: 120,
  });
  await expect(page.getByRole("link", { name: "Önizle" })).toHaveAttribute("href", `/r/${slug}`);

  await visit(visitor, `/r/${slug}`, "Online randevu · açık");
  await expect(visitor.getByRole("button", { name: /Saç kesimi/ })).toBeVisible();
  await expect(visitor.getByText("300,00 TL")).toBeVisible();
});
