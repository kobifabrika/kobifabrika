import { addMember, createContact } from "./support/api";
import { at, book, createService, createStaff, openAllDay, workAllDay } from "./support/appointments";
import { istanbulDay } from "./support/finance";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

test.use({ ownerSector: "HairBarber" });

const longDay = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", weekday: "long", day: "numeric", month: "long" });

/**
 * Sprint 7A izin: personel telefondan izin talep eder (gün sayısı ve çakışan randevu önizlemesi) → Sahip onay penceresinde çakışan
 * randevuyu görür ve onaylar → personele bildirim, takvimde "İzin", online randevuda o gün o personel yok, izin takviminde görünür →
 * Sahip izni iptal eder, gün yeniden açılır.
 */
test("izin: telefondan talep, çakışan randevu uyarısıyla onay, takvimde ve online kapalı, iptal", async ({
  owner,
  ownerPage: page,
  newPublicPage,
  visit,
  check,
  pageAs,
}) => {
  await openAllDay(owner);
  const service = await createService(owner, "Saç kesimi", 30, 350);
  const staffUser = await addMember(owner, "Staff", "Personel Ece");
  const { user } = await staffUser.get<{ user: { id: string } }>("/api/auth/me");
  const ece = await createStaff(owner, "Ece Kalfa", user.id);
  await workAllDay(owner, ece.id);
  const { slug } = await owner.put<{ slug: string }>("/api/business/booking-settings", {
    enabled: true,
    autoConfirm: true,
    minNoticeMinutes: 0,
    maxDaysAhead: 30,
    slotMinutes: 30,
    reminderHoursBefore: 24,
    customerCancelHours: 2,
  });
  const contact = await createContact(owner, { firstName: "Zeynep", lastName: "Kaya" });
  const day = istanbulDay(2);
  await book(owner, { contactId: contact.id, staffId: ece.id, serviceId: service.id, startAt: at(day, "10:00") });

  // Personel telefondan talep eder.
  const staffPage = await pageAs(staffUser);
  await staffPage.goto("/panel/mesai-izin?sekme=izinler");
  await expect(staffPage.getByRole("link", { name: "İzinlerim" })).toHaveAttribute("aria-current", "page");
  await staffPage.getByRole("link", { name: "İzin talep et" }).click();
  await expect(staffPage).toHaveURL(/\/panel\/mesai-izin\/izin-talebi$/);
  await staffPage.getByLabel("İlk gün").fill(day);
  await staffPage.getByLabel("Son gün").fill(day);
  await staffPage.getByLabel("Not (isteğe bağlı)").fill("Aile ziyareti");
  await expect(staffPage.getByText(/Sağlık bilgisi veya tanı yazmayın/)).toBeVisible();
  const conflicts = staffPage.getByRole("region", { name: "Çakışan randevular" });
  await expect(conflicts.getByText(/Zeynep Kaya/)).toBeVisible();
  await expect(staffPage.getByText("1 gün", { exact: true })).toBeVisible();
  await check(staffPage, "İzin talebi · önizleme");
  await staffPage.getByRole("button", { name: "Talebi gönder" }).click();
  await expect(toast(staffPage, /İzin talebiniz gönderildi/)).toBeVisible();
  await expect(staffPage).toHaveURL(/sekme=izinler/);
  await expect(staffPage.getByText("Bekliyor").first()).toBeVisible();
  await check(staffPage, "İzinlerim · bekleyen talep");

  // Sahip onaylar: çakışan randevu uyarısı.
  await visit(page, "/panel/mesai-izin?sekme=izinler", "İzinler · bekleyen (yönetici)");
  await expect(page.getByRole("button", { name: "Bekleyen 1" })).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "Karar ver" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText(/otomatik iptal edilmez/)).toBeVisible();
  await expect(dialog.getByText(/Zeynep Kaya/)).toBeVisible();
  await expect(dialog.getByText(/0 gün → -1 gün/)).toBeVisible();
  await check(page, "İzin · onay penceresi");
  await dialog.getByRole("button", { name: "Onayla" }).click();
  await expect(toast(page, /İzin onaylandı/)).toBeVisible();
  const notes = await staffUser.get<{ items: { title: string }[] }>("/api/notifications");
  expect(notes.items.map((n) => n.title)).toContain("İzin talebiniz onaylandı");

  // Takvimde izin; randevu iptal edilmedi.
  await visit(page, `/panel/randevular?tarih=${day}&personel=${ece.id}`, "Takvim · izinli personel");
  await expect(page.getByText("İzin", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: /Zeynep Kaya/ }).first()).toBeVisible();

  // Online randevuda o gün boş saat yok.
  const customer = await newPublicPage();
  await customer.goto(`/r/${slug}`);
  await customer.getByRole("button", { name: /Saç kesimi/ }).click();
  await customer.getByRole("button", { name: /Ece Kalfa/ }).click();
  const dayLabel = longDay.format(new Date(`${day}T00:00:00Z`));
  await expect(customer.getByRole("radio", { name: `${dayLabel}, boş saat yok` })).toBeDisabled();

  // İzin takvimi.
  await visit(page, `/panel/mesai-izin?sekme=takvim&ay=${day.slice(0, 7)}`, "İzin takvimi");
  // Masaüstünde ızgara hücresi (role=img, etiketli), telefonda gün listesi.
  await expect(page.getByRole("img", { name: /Ece Kalfa .*Yıllık izin/ }).or(page.getByText(/Ece Kalfa · Yıllık izin/)).first()).toBeVisible();
  await check(staffPage, "İzinlerim · onaylı");

  // Sahip izni iptal eder; gün yeniden açılır.
  await visit(page, "/panel/mesai-izin?sekme=izinler", "İzinler · tümü");
  await page.getByRole("button", { name: "Tümü" }).click();
  page.once("dialog", (d) => d.accept());
  await page.getByRole("button", { name: "İzni iptal et" }).click();
  await expect(toast(page, /İzin iptal edildi/)).toBeVisible();
  await customer.reload();
  await customer.getByRole("button", { name: /Saç kesimi/ }).click();
  await customer.getByRole("button", { name: /Ece Kalfa/ }).click();
  await expect(customer.getByRole("radio", { name: dayLabel, exact: true })).toBeEnabled();
});
