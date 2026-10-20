import { platformAdmin } from "./support/api";
import { toast } from "./support/nav";
import { expect, isDesktop, test } from "./support/test";

/**
 * Sprint 8A süper admin ve geri bildirim: Sahip Yardım menüsünden (masaüstü "?", telefonda Menü) WhatsApp destek linkini görür ve
 * geri bildirim gönderir; /platform'a giremez ("Yetkiniz yok"). Süper admin özet → işletme ara → ayrıntı (pilot aç/kapa, not,
 * kullanıcılar, günlük) → geri bildirimi "Çözüldü" yapar; Sahip'e bildirim düşer.
 */
test("süper admin: işletme, pilot, not, geri bildirim", async ({
  owner,
  ownerPage: page,
  pageAs,
  visit,
  check,
}, info) => {
  const business = await owner.get<{ id: string; name: string }>("/api/business");

  // --- Sahip: Yardım → geri bildirim. ---
  await visit(page, "/panel", "Bugün");
  if (isDesktop(info)) {
    await page.getByRole("button", { name: "Yardım" }).click();
    await expect(page.getByRole("menuitem", { name: /WhatsApp destek/ })).toHaveAttribute(
      "href",
      /^https:\/\/wa\.me\/905359779382\?text=/,
    );
    await check(page, "Yardım menüsü");
    await page.getByRole("menuitem", { name: "Geri bildirim gönder" }).click();
  } else {
    await page.getByRole("banner").getByRole("button", { name: "Menü" }).click();
    const menu = page.getByRole("dialog");
    await expect(menu.getByRole("link", { name: /WhatsApp destek/ })).toHaveAttribute("href", /^https:\/\/wa\.me\//);
    await menu.getByRole("button", { name: "Geri bildirim gönder" }).click();
  }
  const dialog = page.getByRole("dialog", { name: "Geri bildirim gönder" });
  await dialog.getByLabel("Mesajınız").fill("kısa");
  await dialog.getByRole("button", { name: "Gönder" }).click();
  await expect(dialog.getByText("En az 10 karakter yazın.")).toBeVisible();
  const body = `Takvimde personel rengi değişmiyor (${info.project.name})`;
  await dialog.getByLabel("Mesajınız").fill(body);
  await check(page, "Geri bildirim penceresi");
  await dialog.getByRole("button", { name: "Gönder" }).click();
  await expect(toast(page, "Geri bildiriminiz alındı. Teşekkür ederiz!")).toBeVisible();

  // Sahip /platform'a giremez.
  await visit(page, "/platform", "Süper admin · yetkisiz");
  await expect(page.getByRole("heading", { name: "Yetkiniz yok" })).toBeVisible();

  // --- Süper admin. ---
  const admin = await platformAdmin();
  const adminPage = await pageAs(admin);
  await visit(adminPage, "/platform", "Süper admin · özet");
  await expect(adminPage.getByRole("heading", { name: "Özet" })).toBeVisible();

  await visit(adminPage, "/platform/isletmeler", "Süper admin · işletmeler");
  await adminPage.getByRole("searchbox", { name: "İşletme ara" }).fill(business.name);
  await adminPage.getByRole("link", { name: business.name }).first().click();
  await expect(adminPage.getByRole("heading", { name: business.name })).toBeVisible();
  await check(adminPage, "Süper admin · işletme ayrıntısı");

  // Yeni işletme denemede; pilot aç → etiket "Pilot".
  const pilot = adminPage.getByRole("switch", { name: "Pilot" });
  await expect(pilot).not.toBeChecked();
  await pilot.click();
  await expect(toast(adminPage, "Pilot durumu değişti.")).toBeVisible();
  await expect(pilot).toBeChecked();
  await expect(adminPage.getByText(/^Pilot · \/r\//)).toBeVisible();

  await adminPage.getByRole("link", { name: "Notlar" }).click();
  await adminPage.getByLabel("Destek notu (işletme görmez)").fill("Pilot görüşmesi yapıldı.");
  await adminPage.getByRole("button", { name: "Not ekle" }).click();
  await expect(adminPage.getByText("Pilot görüşmesi yapıldı.")).toBeVisible();
  await check(adminPage, "Süper admin · notlar");

  await adminPage.getByRole("link", { name: "Kullanıcılar" }).click();
  await expect(adminPage.getByRole("listitem").filter({ hasText: owner.email })).toBeVisible();
  await adminPage.getByRole("link", { name: "Günlük" }).click();
  await expect(adminPage.getByText("Pilot değişti")).toBeVisible();
  await expect(adminPage.getByText("Not eklendi")).toBeVisible();
  await check(adminPage, "Süper admin · günlük");

  // Geri bildirim: yeni → Çözüldü.
  await visit(adminPage, "/platform/geri-bildirim", "Süper admin · geri bildirim");
  await adminPage.getByRole("button", { name: new RegExp(body.replace(/[()]/g, "\\$&")) }).click();
  const detail = adminPage.getByRole("dialog");
  await expect(detail.getByText(business.name).first()).toBeVisible();
  await detail.getByLabel("Durum").selectOption("Resolved");
  await check(adminPage, "Süper admin · geri bildirim ayrıntısı");
  await detail.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(adminPage, "Kaydedildi. Kullanıcıya bildirim gitti.")).toBeVisible();

  // İşletmenin aktivite günlüğünde destek işlemi görünür.
  await visit(page, "/panel/ayarlar/aktivite", "Aktivite günlüğü · destek işlemi");
  await expect(page.getByText("KobiFabrika destek hesabı değiştirdi").filter({ visible: true }).first()).toBeVisible();
});
