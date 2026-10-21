import { addMember, PASSWORD, uniqueEmail } from "./support/api";
import { linkFromEmail } from "./support/mailpit";
import { closeMenu, mainMenu, toast } from "./support/nav";
import { expect, test } from "./support/test";

/** Sprint 1, ekip: davet → personel kaydı → Personel kısıtları → Mali Müşavir salt okunur → çıkarılınca erişim kesilir. */
test.use({ ownerConfirmed: true });

test("davet, personel kaydı, rol kısıtları ve ekipten çıkarma", async ({
  owner,
  ownerPage,
  newPublicPage,
  pageAs,
  visit,
  check,
  guard,
}, info) => {
  // Sahip panelden Personel davet eder.
  const staffEmail = uniqueEmail("personel");
  await visit(ownerPage, "/panel/ayarlar/ekip", "Ekip");
  await ownerPage.getByLabel("E-posta").fill(staffEmail);
  await ownerPage.getByLabel("Rol").selectOption({ label: "Personel" });
  await ownerPage.getByRole("button", { name: "Davet gönder" }).click();
  await expect(toast(ownerPage, /adresine davet gönderildi/)).toBeVisible();
  await expect(ownerPage.getByText(staffEmail, { exact: true })).toBeVisible();

  // Davet e-postasındaki linkten kayıt (ayrı tarayıcı: personelin telefonu).
  const staff = await newPublicPage();
  await staff.goto(await linkFromEmail(staffEmail, /davet etti/, "/davet/"));
  await expect(staff.getByRole("heading", { name: "Ekip daveti" })).toBeVisible();
  await check(staff, "Davet · kayıt");
  await staff.getByLabel("Ad soyad").fill("Zeynep Personel");
  await staff.getByLabel("Cep telefonu").fill("0533 222 33 44");
  await staff.getByLabel("Şifre", { exact: true }).fill(PASSWORD);
  await staff.locator("#terms").check();
  await staff.getByRole("button", { name: "Hesap oluştur ve katıl" }).click();
  await expect(staff).toHaveURL(/\/panel$/);
  await check(staff, "Personel · Bugün");

  // Personel menüde Ayarlar ve Raporlar'ı görmez; adresi yazınca yetki mesajı.
  const menu = await mainMenu(staff, info);
  await expect(menu.getByRole("link", { name: "Müşteriler" })).toBeVisible();
  await expect(menu.getByRole("link", { name: "Ayarlar" })).toHaveCount(0);
  await expect(menu.getByRole("link", { name: "Raporlar" })).toHaveCount(0);
  await closeMenu(staff, info);
  await staff.goto("/panel/ayarlar");
  await expect(staff.getByText("Bu sayfayı görme yetkiniz yok")).toBeVisible();

  // Sahip ekipte yeni üyeyi görür.
  await ownerPage.reload();
  await expect(ownerPage.getByText("Zeynep Personel", { exact: true })).toBeVisible();

  // Mali Müşavir: ayarları salt okunur görür, müşteri ekleyemez.
  const accountant = await pageAs(await addMember(owner, "Accountant", "Mehmet Müşavir"));
  await visit(accountant, "/panel/ayarlar", "Mali Müşavir · Ayarlar");
  await expect(accountant.getByText("Mali müşavir olarak ayarları sadece görüntüleyebilirsiniz.")).toBeVisible();
  await expect(accountant.getByLabel("İşletme adı")).toBeDisabled();
  await visit(accountant, "/panel/musteriler", "Mali Müşavir · Müşteriler");
  await expect(accountant.getByRole("link", { name: "Yeni müşteri" })).toHaveCount(0);
  await ownerPage.goto("/panel/musteriler");
  await expect(ownerPage.getByRole("link", { name: "Yeni müşteri" }).first()).toBeVisible();

  // Sahip personeli ekipten çıkarır → personelin erişimi anında kesilir.
  await ownerPage.goto("/panel/ayarlar/ekip");
  await ownerPage.getByRole("button", { name: "Zeynep Personel kişisini ekipten çıkar" }).click();
  await ownerPage.getByRole("button", { name: "Ekipten çıkar" }).click();
  await expect(ownerPage.getByText("Zeynep Personel", { exact: true })).toHaveCount(0);

  guard.allow(401, /localhost:5180\/api\//);
  guard.allow(403, /localhost:5180\/api\//);
  await staff.goto("/panel/musteriler");
  await expect(staff).toHaveURL(/\/kurulum|\/giris/);
  await expect(staff.getByText("Ayşe")).toHaveCount(0);
  const contacts = await staff.request.get("http://localhost:5180/api/contacts");
  expect([401, 403]).toContain(contacts.status());
});
