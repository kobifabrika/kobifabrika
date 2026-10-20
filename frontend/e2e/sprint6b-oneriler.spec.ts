import { createContact, line, uniquePhone } from "./support/api";
import { createService, createStaff, openAllDay, workAllDay } from "./support/appointments";
import { expect, test } from "./support/test";

test.use({ ownerSector: "HairBarber" });

const sms = { sms: true, email: false, call: false };

/** İstanbul günü, bugünden n gün sonra ("2026-10-16"). */
function istanbulDay(offset: number) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(new Date(Date.now() + offset * 86_400_000));
}

/**
 * Sprint 6B öneriler: Bugün'de "Yarın boş saatler" → Kampanya → sihirbaz hazır mesajla; takvimde yarın görünümünde öneri şeridi;
 * "Kaybedilme riski" kartı (en az 3 ziyaret, ortalamanın 2 katından uzun) → Listeyi gör (segment çipi) → kampanya.
 */
test("Bugün: boş saat ve kaybedilme riski önerileri @sms", async ({ owner, ownerPage: page, visit, check }) => {
  await openAllDay(owner);
  await createService(owner, "Saç kesimi", 45);
  const staff = await createStaff(owner, "Ayşe Usta");
  await workAllDay(owner, staff.id);

  // Düzenli müşteri: 60, 50 ve 40 gün önce satış → ortalama 10 gün, son ziyaret 40 gün önce (eşik 20) → risk.
  const risky = await createContact(owner, { firstName: "Zeynep", lastName: "Kaya", phone: uniquePhone(), consents: sms });
  for (const daysAgo of [60, 50, 40]) {
    await owner.post("/api/sales", {
      contactId: risky.id,
      date: `${istanbulDay(-daysAgo)}T09:00:00Z`,
      lines: [line(null, 1, { unitPrice: 200, description: "Bakım" })],
      discount: null,
      payments: [{ method: "Cash", amount: 200, cashAccountId: null }],
      note: null,
    });
  }

  await visit(page, "/panel", "Bugün · öneri kartları");
  const slots = page.getByRole("list", { name: "Yarın boş saatler" });
  await expect(slots.getByText("Ayşe Usta boş").first()).toBeVisible();
  const risk = page.getByRole("list", { name: "Kaybedilme riskindeki müşteriler" });
  await expect(risk.getByRole("link", { name: "Zeynep Kaya" })).toBeVisible();
  await expect(page.getByText("1 düzenli müşteri her zamankinden uzun süredir gelmedi.")).toBeVisible();

  // Boş saatten kampanya: sihirbaz hazır mesajla.
  await slots.getByRole("link", { name: /kampanya başlat/ }).first().click();
  await expect(page.getByRole("heading", { name: "Yeni kampanya" })).toBeVisible();
  await expect(page.getByText("1 kişiye gidecek")).toBeVisible();
  await page.getByRole("button", { name: "Devam" }).click();
  await expect(page.getByRole("textbox", { name: "Mesaj" })).toHaveValue(/^Merhaba \{ad\}, yarın \d{2}:\d{2}-\d{2}:\d{2} arası \{isletme\}'de yerimiz var\./);
  await expect(page.getByLabel("Kampanya adı")).toHaveValue(/^Boş saat /);
  await check(page, "Yeni kampanya · boş saatten");

  // Takvim, yarın: öneri şeridi.
  await visit(page, `/panel/randevular?tarih=${istanbulDay(1)}`, "Takvim · yarın, boş saat önerisi");
  await expect(page.getByRole("region", { name: "Boş saat önerisi" }).getByText(/Ayşe Usta boş/).first()).toBeVisible();

  // Risk listesi: Bugün → Listeyi gör → segment çipi seçili → kampanya.
  await visit(page, "/panel", "Bugün · tekrar");
  await page.getByRole("link", { name: "Listeyi gör" }).click();
  await expect(page.getByRole("button", { name: "Kaybedilme riski" })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("link", { name: /Zeynep Kaya/ }).first()).toBeVisible();
  await check(page, "Müşteriler · kaybedilme riski");
  await page.getByRole("link", { name: "Bu segmente kampanya" }).click();
  await expect(page.getByRole("button", { name: "Kaybedilme riski" })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByText("1 kişiye gidecek")).toBeVisible();
  await page.getByRole("button", { name: "Devam" }).click();
  await expect(page.getByRole("textbox", { name: "Mesaj" })).toHaveValue(/sizi özledik/);
});
