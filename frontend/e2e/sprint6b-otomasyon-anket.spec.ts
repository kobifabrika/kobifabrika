import { createContact, uniquePhone, type ApiUser } from "./support/api";
import { book, createService, createStaff, openAllDay, pastSlot, workAllDay } from "./support/appointments";
import { smsTexts } from "./support/mailpit";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

test.use({ ownerSector: "HairBarber" });

const sms = { sms: true, email: false, call: false };

/** Tamamlanmış bir randevu (müşteri izinli). Dönüş: müşterinin telefonu. */
async function completedVisit(owner: ApiUser, serviceId: string, staffId: string, first: string, minutesAgo: number) {
  const phone = uniquePhone();
  const contact = await createContact(owner, { firstName: first, lastName: "Yılmaz", phone, consents: sms });
  const slot = pastSlot(minutesAgo);
  const appointment = await book(owner, { contactId: contact.id, staffId, serviceId, startAt: slot.iso });
  await owner.post(`/api/appointments/${appointment.id}/complete`);
  return phone;
}

/** Anket SMS'i gelene kadar bekler (otomasyon E2E'de dakikada bir), /a/ yolunu döner. */
async function surveyPath(phone: string): Promise<string> {
  let path = "";
  await expect
    .poll(
      async () => {
        const text = (await smsTexts(phone)).find((t) => t.includes("/a/"));
        path = text?.match(/\/a\/[A-Za-z0-9]{10}/)?.[0] ?? "";
        return path;
      },
      { timeout: 150_000, intervals: [3_000] },
    )
    .not.toBe("");
  return path;
}

/**
 * Sprint 6B: Kampanyalar → Otomasyonlar (teşekkür ve anket aç), tamamlanan randevu → Mailpit'te tek SMS (teşekkür + anket linki)
 * → /a/[kod]'da 2 puan → yorum → işletmeye bildirim; başka müşteri 5 puan → Google düğmesi önde. Anketler sekmesinde özet.
 */
test("otomasyon: teşekkür + anket, puan ve yorum, Google linki", async ({ owner, ownerPage: page, newPublicPage, visit, check }) => {
  test.setTimeout(240_000);
  await openAllDay(owner);
  const service = await createService(owner, "Saç kesimi", 30);
  const staff = await createStaff(owner, "Ayşe Usta");
  await workAllDay(owner, staff.id);

  await visit(page, "/panel/kampanyalar?sekme=otomasyonlar", "Kampanyalar · Otomasyonlar");
  const list = page.getByRole("list", { name: "Otomasyonlar" });
  await expect(list.getByText("Doğum günü", { exact: true })).toBeVisible();
  await expect(list.getByRole("switch", { name: "Memnuniyet anketi açık" })).not.toBeChecked();

  // Teşekkür: düzenle → aç, hemen, kaydet.
  await page.getByRole("link", { name: "Randevu sonrası teşekkür: düzenle" }).click();
  await expect(page.getByRole("heading", { name: "Randevu sonrası teşekkür" })).toBeVisible();
  await page.getByRole("switch", { name: "Otomasyon açık" }).click();
  await page.getByRole("combobox", { name: "Ne zaman" }).selectOption({ label: "Hemen" });
  await page.getByRole("button", { name: "Önizle" }).click();
  await expect(page.getByRole("region", { name: "Önizleme" })).toContainText("Ayşe, bugün");
  await check(page, "Otomasyon · teşekkür");
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Otomasyon kaydedildi.")).toBeVisible();

  // Anket: listeden aç (anahtar), sonra düzenle → hemen.
  await page.getByRole("link", { name: "Memnuniyet anketi: düzenle" }).click();
  await page.getByRole("switch", { name: "Otomasyon açık" }).click();
  await page.getByRole("combobox", { name: "Ne zaman" }).selectOption({ label: "Hemen" });
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(page.getByRole("switch", { name: "Memnuniyet anketi açık" })).toBeChecked();

  // Google yorum linki (Anketler sekmesi).
  await visit(page, "/panel/kampanyalar?sekme=anketler", "Kampanyalar · Anketler (boş)");
  await page.getByLabel("Google yorum linki").fill("https://g.page/r/ornek-isletme/review");
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Google yorum linki kaydedildi.")).toBeVisible();

  // İki tamamlanan randevu → tek SMS'te teşekkür + anket.
  const lowPhone = await completedVisit(owner, service.id, staff.id, "Deniz", 120);
  const highPhone = await completedVisit(owner, service.id, staff.id, "Ece", 60);
  const lowPath = await surveyPath(lowPhone);
  const lowText = (await smsTexts(lowPhone)).find((t) => t.includes("/a/"))!;
  expect(lowText).toMatch(/^Deniz, bugün .+ teşekkür ederiz\. Deniz, .+ puanlar mısınız\? http/);
  expect(lowText).toContain("SMS almamak icin:");

  // 2 puan → yorum kutusu önde, Google altta sade.
  const customer = await newPublicPage();
  await visit(customer, lowPath, "Anket · puan");
  await expect(customer.getByRole("heading", { name: "Ziyaretinizi nasıl buldunuz?" })).toBeVisible();
  await customer.getByRole("button", { name: "2 puan" }).click();
  await expect(customer.getByText("Puanınız:")).toBeVisible();
  await customer.getByLabel("Bize iletin: neyi daha iyi yapabiliriz?").fill("Biraz bekledim.");
  await expect(customer.getByRole("link", { name: "Google'da yorum yaz" })).toBeVisible();
  await check(customer, "Anket · 2 puan, yorum");
  await customer.getByRole("button", { name: "Gönder" }).click();
  await expect(customer.getByText(/Yorumunuz .+ iletildi\./)).toBeVisible();

  // İşletmeye bildirim.
  await expect
    .poll(async () => (await owner.get<{ items: { type: string }[] }>("/api/notifications")).items.map((n) => n.type))
    .toContain("survey.comment");

  // 5 puan → Google düğmesi önde, yorum kutusu yok.
  const highPath = await surveyPath(highPhone);
  const happy = await newPublicPage();
  await visit(happy, highPath, "Anket · ikinci müşteri");
  await happy.getByRole("button", { name: "5 puan" }).click();
  await expect(happy.getByText(/Memnun kaldıysanız/)).toBeVisible();
  const google = happy.getByRole("link", { name: "Google'da yorum yaz" });
  await expect(google).toHaveAttribute("href", "https://g.page/r/ornek-isletme/review");
  await expect(happy.getByLabel("Bize iletin: neyi daha iyi yapabiliriz?")).toHaveCount(0);
  await check(happy, "Anket · 5 puan, Google");

  // Anketler sekmesi: ortalama 3,5, yorum görünür.
  await visit(page, "/panel/kampanyalar?sekme=anketler", "Kampanyalar · Anketler");
  await expect(page.getByRole("region", { name: "Anket özeti (son 90 gün)" })).toContainText("3,5");
  await expect(page.getByText("Biraz bekledim.")).toBeVisible();
});
