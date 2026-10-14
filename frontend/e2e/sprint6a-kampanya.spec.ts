import { addMember, createContact, platformAdmin, uniquePhone } from "./support/api";
import { smsTexts } from "./support/mailpit";
import { mainMenu, toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

const sms = (on: boolean) => ({ sms: on, email: false, call: false });

/**
 * Sprint 6A: kredi yükle (platform) → Ayarlar · SMS (bakiye, hareket, "Kredi yükle" penceresi) → kampanya sihirbazı
 * (hedef, mesaj sayacı, önizleme) → gönderim → Mailpit'te SMS ve çıkış linki → /c/[kod] "SMS'leri durdur" → müşteri kartında
 * SMS izni kapalı (çıkış linkinden).
 */
test("kredi yükle, kampanya gönder, çıkış linki", async ({ owner, ownerPage: page, pageAs, newPublicPage, visit, check }, info) => {
  const business = await owner.get<{ name: string }>("/api/business");
  const aysePhone = uniquePhone();
  const ayse = await createContact(owner, { firstName: "Ayşe", lastName: "Yılmaz", phone: aysePhone, consents: sms(true) });
  await createContact(owner, { firstName: "Elif", lastName: "Kara", consents: sms(true) });
  await createContact(owner, { firstName: "Zeynep", lastName: "Ak", consents: sms(false) });

  // Platform yöneticisi kredi yükler.
  const admin = await platformAdmin();
  const adminPage = await pageAs(admin);
  await visit(adminPage, "/panel/platform/kredi", "Platform · kredi yükle");
  await adminPage.getByRole("searchbox", { name: "İşletme ara" }).fill(business.name);
  await adminPage.getByRole("button", { name: new RegExp(business.name) }).click();
  await adminPage.getByLabel("Kredi miktarı").fill("100");
  await adminPage.getByLabel("Not", { exact: true }).fill("E2E havale");
  await check(adminPage, "Platform · kredi yükle · seçili");
  await adminPage.getByRole("button", { name: "Yükle" }).click();
  await expect(toast(adminPage, /100 kredi yüklendi\. Yeni bakiye 150/)).toBeVisible();

  // Ayarlar · SMS: 50 hoş geldin + 100 yükleme.
  await visit(page, "/panel/ayarlar/sms", "Ayarlar · SMS");
  await expect(page.getByTestId("sms-balance")).toHaveText("150");
  const history = page.getByRole("list", { name: "Kredi hareketleri" });
  await expect(history.getByText("Yükleme")).toBeVisible();
  await expect(history.getByText("Hoş geldin kredisi")).toBeVisible();
  await page.getByRole("button", { name: "Kredi yükle" }).first().click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("Kredi yüklemek için KobiFabrika destek ekibine ulaşın.")).toBeVisible();
  await check(page, "Ayarlar · SMS · kredi yükle");
  await page.keyboard.press("Escape");

  // Menüden Kampanyalar.
  const nav = await mainMenu(page, info);
  await nav.getByRole("link", { name: "Kampanyalar" }).click();
  await expect(page.getByRole("heading", { name: "Kampanyalar" })).toBeVisible();
  await expect(page.getByText("Henüz kampanya yok")).toBeVisible();
  await check(page, "Kampanyalar · boş");

  // Sihirbaz: hedef → mesaj → önizleme.
  await page.getByRole("link", { name: "Yeni kampanya" }).click();
  await expect(page.getByText("2 kişiye gidecek")).toBeVisible();
  await expect(page.getByText(/Segmentte 3 müşteri · 1 elendi/)).toBeVisible();
  await check(page, "Yeni kampanya · kime");
  await page.getByRole("button", { name: "Devam" }).click();

  await page.getByLabel("Kampanya adı").fill("Ekim indirimi");
  const body = page.getByRole("textbox", { name: "Mesaj" });
  await body.fill("Merhaba ");
  await page.getByRole("button", { name: "+ Ad ({ad})" }).click();
  await body.press("End");
  await body.pressSequentially(", bu hafta %20 indirim var.");
  await expect(body).toHaveValue("Merhaba {ad}, bu hafta %20 indirim var.");
  await expect(page.getByText(/karakter · 2 SMS \(kişi başı\) · Türkçe karakter var/)).toBeVisible();
  await expect(page.getByText(/SMS almamak icin: .*\/c\/… veya RET/)).toBeVisible();
  await check(page, "Yeni kampanya · mesaj");
  await page.getByRole("button", { name: "Önizle" }).click();

  const samples = page.getByRole("region", { name: "Örnek mesajlar" });
  await expect(samples.getByText(/Merhaba Ayşe, bu hafta %20 indirim var\. SMS almamak icin:/)).toBeVisible();
  await expect(page.getByText("İzni yok")).toBeVisible();
  await expect(page.getByText("Tahmini kredi")).toBeVisible();
  await check(page, "Yeni kampanya · önizleme");
  await page.getByRole("button", { name: "Gönder", exact: true }).click();

  // Ayrıntı: gönderim Hangfire ile; tamamlanınca sayılar.
  await expect(page).toHaveURL(/\/panel\/kampanyalar\/[0-9a-f-]+$/);
  await expect(page.getByRole("heading", { name: "Ekim indirimi" })).toBeVisible();
  await expect(page.getByText("Tamamlandı").first()).toBeVisible({ timeout: 90_000 });
  await expect(page.getByRole("list", { name: "Alıcı listesi" }).getByText("SMS izni yok")).toBeVisible();
  await check(page, "Kampanya · ayrıntı");

  // Mailpit: SMS ve çıkış linki.
  let link = "";
  await expect
    .poll(async () => {
      const text = (await smsTexts(aysePhone)).find((t) => t.includes("/c/"));
      link = text?.match(/https?:\/\/\S+\/c\/[A-Za-z0-9]{8}/)?.[0] ?? "";
      return link;
    })
    .not.toBe("");

  const customer = await newPublicPage();
  await visit(customer, new URL(link).pathname, "Çıkış linki");
  await expect(customer.getByRole("heading", { name: "Kampanya SMS'lerini durdurmak istiyor musunuz?" })).toBeVisible();
  await expect(customer.getByText(business.name).first()).toBeVisible();
  await customer.getByRole("button", { name: "SMS'leri durdur" }).click();
  await expect(customer.getByText(`Artık ${business.name}'den kampanya SMS'i almayacaksınız.`)).toBeVisible();
  await check(customer, "Çıkış linki · durduruldu");

  // Müşteri kartında SMS izni kapalı.
  await visit(page, `/panel/musteriler/${ayse.id}`, "Müşteri kartı · çıkış sonrası");
  await expect(page.getByText(/çıkış linkinden/)).toBeVisible();

  if (isDesktop(info)) {
    // Personel kampanyaları görmez; Mali Müşavir salt okunur görür.
    const staff = await addMember(owner, "Staff");
    const staffPage = await pageAs(staff);
    await visit(staffPage, "/panel", "Bugün · Personel");
    await expect((await mainMenu(staffPage, info)).getByRole("link", { name: "Kampanyalar" })).toHaveCount(0);
    const accountant = await addMember(owner, "Accountant");
    const accPage = await pageAs(accountant);
    await visit(accPage, "/panel/kampanyalar", "Kampanyalar · Mali Müşavir");
    await expect(accPage.getByRole("link", { name: "Ekim indirimi" })).toBeVisible();
    await expect(accPage.getByRole("link", { name: "Yeni kampanya" })).toHaveCount(0);
  }
});
