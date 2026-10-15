import { createContact, newApiContext, uniquePhone } from "./support/api";
import { book, createService, createStaff, openAllDay, pastSlot, workAllDay } from "./support/appointments";
import { istanbulDay } from "./support/finance";
import { smsTexts } from "./support/mailpit";
import { expect, test } from "./support/test";

test.use({ ownerSector: "HairBarber" });

const sms = { sms: true, email: false, call: false };

/** Numaraya gelen ve verilen yolu (/c/… veya /a/…) içeren SMS'teki kod (otomasyon ve kampanya Hangfire ile; bekler). */
async function codeFrom(phone: string, pattern: RegExp): Promise<string> {
  let code = "";
  await expect
    .poll(
      async () => {
        code = (await smsTexts(phone)).map((t) => t.match(pattern)?.[1]).find(Boolean) ?? "";
        return code;
      },
      { timeout: 150_000, intervals: [3_000] },
    )
    .not.toBe("");
  return code;
}

/**
 * Sprint 7B raporları: Kampanyalar (gönderilen / alıcı, nedenine göre elenen, kredi, sonradan çıkış yapan), Anket (ortalama,
 * dağılım, personel), Personel (satış, randevu, anket, çalışılan saat). Masaüstünde tablo, telefonda kart; Excel indirilir.
 */
test("raporlar: kampanya, anket ve personel", async ({ owner, ownerPage: page, visit, guard }) => {
  // Anket SMS'i dakikalık otomasyonla gelir (6B senaryosuyla aynı süre).
  test.setTimeout(240_000);
  await openAllDay(owner);
  const service = await createService(owner, "Saç kesimi", 30, 300);
  const ayse = await createStaff(owner, "Ayşe Usta");
  await workAllDay(owner, ayse.id);
  await owner.put("/api/automations/Survey", { isEnabled: true, body: "{ad}, puanlar mısınız? {link}", delayMinutes: 0 });

  // Kampanya: iki izinli müşteri, biri izinsiz; biri SMS'teki linkten çıkar.
  const phones = [uniquePhone(), uniquePhone()];
  await createContact(owner, { firstName: "Deniz", lastName: "Kaya", phone: phones[0], consents: sms });
  await createContact(owner, { firstName: "Selin", lastName: "Kaya", phone: phones[1], consents: sms });
  await createContact(owner, { firstName: "İzinsiz", lastName: "Müşteri" });
  const campaign = await owner.post<{ id: string }>("/api/campaigns", {
    name: "Ekim kampanyası",
    target: { filter: { type: "customer" } },
    body: "Merhaba {ad}, bu hafta indirim var.",
    scheduledAt: null,
  });
  await expect
    .poll(async () => (await owner.get<{ status: string }>(`/api/campaigns/${campaign.id}`)).status, { timeout: 90_000, intervals: [2_000] })
    .toBe("Completed");
  const optOut = await codeFrom(phones[0], /\/c\/([A-Za-z0-9]{8})/);
  const anonymous = await newApiContext();
  expect((await anonymous.post(`/api/public/opt-out/${optOut}`)).ok()).toBe(true);
  await anonymous.dispose();

  // Anket ve personel: Ayşe'nin tamamlanan randevusu → anket SMS'i → 4 puan; 09:00-17:00 mesai (dün).
  const customer = await createContact(owner, { firstName: "Ece", lastName: "Demir", phone: uniquePhone(), consents: sms });
  const appointment = await book(owner, { contactId: customer.id, staffId: ayse.id, serviceId: service.id, startAt: pastSlot(90).iso });
  await owner.post(`/api/appointments/${appointment.id}/complete`);
  const customerPhone = (await owner.get<{ phone: string }>(`/api/contacts/${customer.id}`)).phone;
  const surveyCode = await codeFrom(customerPhone, /\/a\/([A-Za-z0-9]{10})/);
  const publicApi = await newApiContext();
  expect((await publicApi.post(`/api/public/surveys/${surveyCode}/score`, { data: { score: 4 } })).ok()).toBe(true);
  await publicApi.dispose();
  const yesterday = istanbulDay(-1);
  await owner.post("/api/time-entries", {
    staffMemberId: ayse.id,
    clockInAt: `${yesterday}T06:00:00Z`,
    clockOutAt: `${yesterday}T14:00:00Z`,
    note: null,
  });
  const range = `aralik=ozel&from=${yesterday}&to=${istanbulDay()}`;

  await visit(page, `/panel/raporlar?rapor=kampanyalar&${range}`, "Rapor · kampanyalar");
  const campaigns = page.getByRole("region", { name: "Kampanya raporu" });
  await expect(campaigns.getByText("Ekim kampanyası").filter({ visible: true }).first()).toBeVisible();
  await expect(campaigns.getByText("2 / 3").filter({ visible: true }).first()).toBeVisible();
  await expect(campaigns.getByText("1 izni yok").filter({ visible: true }).first()).toBeVisible();
  const row = (await owner.get<{ rows: { optOuts: number | null; creditUsed: number }[] }>(`/api/reports/campaigns?from=${yesterday}&to=${istanbulDay()}`)).rows[0];
  expect(row.optOuts).toBe(1);
  expect(row.creditUsed).toBeGreaterThanOrEqual(2);

  // WebKit indirme isteğini "Frame load interrupted" ile kapatır; indirme yine olur.
  guard.allow("network", /\/api\/reports\/campaigns\.xlsx/);
  const download = page.waitForEvent("download");
  await page.getByRole("link", { name: "Excel'e aktar" }).click();
  expect((await download).suggestedFilename()).toMatch(/^rapor-kampanyalar-.+\.xlsx$/);

  await visit(page, `/panel/raporlar?rapor=anket&${range}`, "Rapor · anket");
  const surveys = page.getByRole("region", { name: "Anket raporu" });
  await expect(surveys.getByText("4,0").first()).toBeVisible();
  await expect(surveys.getByText("1 / 1")).toBeVisible();
  await expect(surveys.getByText("Ayşe Usta").filter({ visible: true }).first()).toBeVisible();

  await visit(page, `/panel/raporlar?rapor=personel&${range}`, "Rapor · personel");
  const staff = page.getByRole("region", { name: "Personel raporu" });
  await expect(staff.getByText("Ayşe Usta").filter({ visible: true }).first()).toBeVisible();
  await expect(staff.getByText("4,0 (1)").filter({ visible: true }).first()).toBeVisible();
  await expect(staff.getByText("8", { exact: true }).filter({ visible: true }).first()).toBeVisible();
});
