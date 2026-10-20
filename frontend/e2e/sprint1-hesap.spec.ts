import { PASSWORD, uniqueEmail } from "./support/api";
import { linkFromEmail } from "./support/mailpit";
import { login, logout } from "./support/nav";
import { expect, test } from "./support/test";

/** Sprint 1: kayıt → e-posta doğrulama (Mailpit) → giriş → kurulum sihirbazı → çıkış/giriş → şifremi unuttum. */
test("kayıt, doğrulama, kurulum sihirbazı, çıkış/giriş ve şifre sıfırlama", async ({ page, visit, check, guard }) => {
  const email = uniqueEmail("kayit");
  const businessName = `Kaya Butik ${Date.now().toString(36)}`;

  // Kayıt
  await visit(page, "/kayit", "Kayıt");
  await page.getByLabel("Ad soyad").fill("Emir Deneme");
  await page.getByLabel("E-posta").fill(email);
  await page.getByLabel("Cep telefonu").fill("0532 123 45 67");
  await page.getByLabel("Şifre", { exact: true }).fill(PASSWORD);
  await page.locator("#terms").check();
  await page.getByRole("button", { name: "Hesap oluştur" }).click();
  await expect(page).toHaveURL(/\/kurulum/);
  await expect(page.getByRole("heading", { name: "İşletmeni tanıyalım" })).toBeVisible();

  // E-posta doğrulama linki (Mailpit)
  const verify = await linkFromEmail(email, /E-posta adresini doğrula/, "/e-posta-dogrula");
  await page.goto(verify);
  await expect(page.getByText("E-postan doğrulandı")).toBeVisible();
  await check(page, "E-posta doğrulandı");

  // Çıkıp giriş ekranından tekrar girince sihirbaz kaldığı yerden açılır.
  await page.context().clearCookies();
  await login(page, email, PASSWORD);
  await expect(page).toHaveURL(/\/kurulum/);

  // Sihirbaz 1. adım: 8 sektör kartı, hiçbirinde yazı taşmıyor.
  const sectors = page.getByRole("radiogroup", { name: "Sektör" }).getByRole("radio");
  await expect(sectors).toHaveCount(8);
  const overflowing = await sectors.evaluateAll((cards) =>
    cards.filter((c) => c.scrollWidth > c.clientWidth + 1 || c.scrollHeight > c.clientHeight + 1).map((c) => c.textContent),
  );
  expect(overflowing, "Taşan sektör kartları").toEqual([]);
  await check(page, "Kurulum 1 · işletme");

  await page.getByLabel("İşletme adı").fill(businessName);
  await page.getByRole("radio", { name: /Perakende\/Mağaza/ }).click();
  await page.getByLabel("İşletme telefonu").fill("0212 555 44 33");
  await page.getByLabel("İl", { exact: true }).selectOption("İstanbul");
  await page.getByLabel("İlçe").selectOption("Kadıköy");
  await page.getByRole("button", { name: "Devam" }).click();

  await expect(page.getByRole("heading", { name: "Çalışma saatlerin" })).toBeVisible();
  await check(page, "Kurulum 2 · çalışma saatleri");
  await page.getByRole("button", { name: "Devam" }).click();

  await expect(page.getByRole("heading", { name: "Logon var mı?" })).toBeVisible();
  await check(page, "Kurulum 3 · logo");
  await page.getByRole("button", { name: "Kurulumu bitir" }).click();

  await expect(page).toHaveURL(/\/panel$/);
  await expect(page.getByText(businessName).filter({ visible: true }).first()).toBeVisible();
  await check(page, "Panel ilk açılış");

  // Çıkış → giriş
  await logout(page);
  await login(page, email, PASSWORD);
  await expect(page).toHaveURL(/\/panel$/);
  await expect(page.getByText(businessName).filter({ visible: true }).first()).toBeVisible();

  // Yanlış şifre: Türkçe hata
  await logout(page);
  guard.allow(401, /\/api\/auth\/login$/);
  await login(page, email, "YanlisSifre1");
  await expect(page.getByText(/hatalı|yanlış/i).first()).toBeVisible();
  await check(page, "Giriş · hatalı şifre");

  // Şifremi unuttum → e-postadaki link → yeni şifre → yeni şifreyle giriş
  await visit(page, "/sifremi-unuttum", "Şifremi unuttum");
  await page.getByLabel("E-posta").fill(email);
  await page.getByRole("button", { name: "Sıfırlama bağlantısı gönder" }).click();
  await expect(page.getByText(/sıfırlama bağlantısı gönderdik/)).toBeVisible();
  const reset = await linkFromEmail(email, /Şifreni sıfırla/, "/sifre-sifirla");
  await page.goto(reset);
  await page.getByLabel("Yeni şifre", { exact: true }).fill("YeniSifre99");
  await page.getByLabel("Yeni şifre (tekrar)").fill("YeniSifre99");
  await check(page, "Yeni şifre");
  await page.getByRole("button", { name: "Şifremi yenile" }).click();
  await expect(page).toHaveURL(/\/giris\?bilgi=sifre-yenilendi/);
  await expect(page.getByText("Şifren yenilendi.")).toBeVisible();

  await login(page, email, "YeniSifre99");
  await expect(page).toHaveURL(/\/panel$/);
});
