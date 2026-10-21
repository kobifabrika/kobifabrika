import { addMember } from "./support/api";
import { createService } from "./support/appointments";
import { mainMenu, toast } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 5A personel: ekleme (renk, online randevu), haftalık saatler (öğle arası), verdiği hizmetler, mola/izin;
 * Ayarlar'da tatil günü ve "Personel sadece kendi randevularını görsün"; Mali Müşavir salt okunur, Personel menüde görmez.
 */
test("personel: ekleme, öğle aralı saatler, hizmetler, kapalı zaman, tatil ve ayar", async ({ owner, ownerPage: page, visit, check, pageAs }, info) => {
  await createService(owner, "Saç kesimi", 45);
  await createService(owner, "Boya", 90);

  await page.goto("/panel");
  const menu = await mainMenu(page, info);
  await menu.getByRole("link", { name: "Personel" }).click();
  await expect(page).toHaveURL(/\/panel\/personel$/);

  await visit(page, "/panel/personel", "Personel · boş");
  await expect(page.getByText("Henüz personel yok")).toBeVisible();
  await page.getByRole("link", { name: "Personel ekle" }).first().click();
  await expect(page).toHaveURL(/\/panel\/personel\/yeni$/);
  await page.getByLabel("Ad soyad").fill("Zeynep Usta");
  await page.getByLabel("Unvan (isteğe bağlı)").fill("Kuaför");
  await page.getByRole("radio", { name: "Mavi" }).click();
  await check(page, "Personel ekle");
  await page.getByRole("button", { name: "Personeli ekle" }).click();
  await expect(toast(page, /Personel eklendi/)).toBeVisible();
  await expect(page).toHaveURL(/\/panel\/personel\/[0-9a-f-]+\?sekme=saatler$/);

  // Pazartesi: 09:00-12:00 ve 13:00-18:00 (öğle arası).
  await page.getByLabel("Pazartesi 1. aralık bitiş").fill("12:00");
  await page.getByRole("button", { name: "Aralık ekle" }).first().click();
  await page.getByLabel("Pazartesi 2. aralık başlangıç").fill("13:00");
  await page.getByLabel("Pazartesi 2. aralık bitiş").fill("18:00");
  await check(page, "Personel · çalışma saatleri");
  await page.getByRole("button", { name: "Saatleri kaydet" }).click();
  await expect(toast(page, "Çalışma saatleri kaydedildi.")).toBeVisible();
  const staffId = page.url().split("/personel/")[1]!.split("?")[0]!;
  const hours = await owner.get<{ days: { day: string; ranges: { start: string; end: string }[] }[] }>(`/api/staff/${staffId}/working-hours`);
  expect(hours.days[0]!.ranges).toEqual([
    { start: "09:00", end: "12:00" },
    { start: "13:00", end: "18:00" },
  ]);

  // Hizmetler: sadece Saç kesimi.
  await page.getByRole("link", { name: "Hizmetler", exact: true }).click();
  await page.getByRole("checkbox").first().check();
  await check(page, "Personel · hizmetler");
  await page.getByRole("button", { name: "Hizmetleri kaydet" }).click();
  await expect(toast(page, "Hizmetler kaydedildi.")).toBeVisible();
  await expect(page.getByText("1 hizmet seçili")).toBeVisible();

  // Kapalı zaman: mola.
  await page.getByRole("link", { name: "Kapalı zamanlar", exact: true }).click();
  await page.getByRole("textbox", { name: "Not (isteğe bağlı)" }).fill("Öğle arası");
  await page.getByRole("button", { name: "Ekle", exact: true }).click();
  await expect(toast(page, "Kapalı zaman eklendi.")).toBeVisible();
  await expect(page.getByText("Mola · Öğle arası")).toBeVisible();
  await check(page, "Personel · kapalı zamanlar");

  await visit(page, "/panel/personel", "Personel · liste");
  await expect(page.getByRole("link", { name: "Zeynep Usta" }).first()).toBeVisible();

  // Ayarlar: tatil günü ve randevu ayarı.
  await visit(page, "/panel/ayarlar/calisma-saatleri", "Ayarlar · çalışma saatleri ve tatiller");
  const holiday = page.locator("[data-slot=card]").filter({ hasText: "Tatil ve kapalı günler" });
  await holiday.getByLabel("Tarih").fill("2027-01-01");
  await holiday.getByLabel("Adı").fill("Yılbaşı");
  await holiday.getByRole("button", { name: "Kapalı gün ekle" }).click();
  await expect(toast(page, "Kapalı gün eklendi.")).toBeVisible();
  await expect(holiday.getByText("Yılbaşı")).toBeVisible();

  await visit(page, "/panel/ayarlar/randevu", "Ayarlar · randevu");
  await page.getByRole("switch", { name: /Personel sadece kendi randevularını görsün/ }).click();
  await expect(toast(page, "Randevu ayarları kaydedildi.")).toBeVisible();
  expect((await owner.get<{ staffSeesOnlyOwn: boolean }>("/api/business/appointment-settings")).staffSeesOnlyOwn).toBe(true);

  // Mali Müşavir salt okunur görür; Personel menüde görmez.
  const accountant = await addMember(owner, "Accountant", "Müşavir Can");
  const accountantPage = await pageAs(accountant);
  await accountantPage.goto(`/panel/personel/${staffId}`);
  await check(accountantPage, "Personel kartı · Mali Müşavir");
  // Devre dışı form değil, okunur özet (2026-10-21).
  await expect(accountantPage.getByRole("textbox")).toHaveCount(0);
  await expect(accountantPage.getByRole("definition").filter({ hasText: "Kuaför" })).toBeVisible();
  await expect(accountantPage.getByRole("button", { name: "Kaydet" })).toHaveCount(0);
  await accountantPage.getByRole("link", { name: "Çalışma saatleri", exact: true }).click();
  await expect(accountantPage.getByText("09:00–12:00, 13:00–18:00")).toBeVisible();
  await check(accountantPage, "Personel kartı · Mali Müşavir · saatler");
  await accountantPage.getByRole("link", { name: "Hizmetler", exact: true }).click();
  await expect(accountantPage.getByRole("checkbox")).toHaveCount(0);
  await expect(accountantPage.getByRole("listitem").filter({ hasText: " dk" })).toHaveCount(1);

  const staff = await addMember(owner, "Staff", "Personel Ece");
  const staffPage = await pageAs(staff);
  await staffPage.goto("/panel");
  const staffMenu = await mainMenu(staffPage, info);
  await expect(staffMenu.getByRole("link", { name: "Personel" })).toHaveCount(0);
});
