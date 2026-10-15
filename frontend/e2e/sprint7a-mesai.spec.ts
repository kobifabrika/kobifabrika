import { addMember } from "./support/api";
import { createStaff } from "./support/appointments";
import { istanbulDay } from "./support/finance";
import { mainMenu, toast } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 7A mesai: personel kendi telefonundan "Mesaiye başla" → durum → "Mesaiyi bitir"; yönetici gün görünümünde görür, düzeltir,
 * başkası adına ekler; mesai raporu (haftalık fazla mesai, yasal not); Personel rolü başkasının kaydını görmez.
 */
test("mesai: personel telefondan başlar ve bitirir, yönetici düzeltir ve ekler, rapor", async ({ owner, ownerPage: page, visit, check, pageAs }, info) => {
  const staffUser = await addMember(owner, "Staff", "Personel Ece");
  const { user } = await staffUser.get<{ user: { id: string } }>("/api/auth/me");
  const ece = await createStaff(owner, "Ece Kalfa", user.id);
  await createStaff(owner, "Mehmet Usta");

  // Personel: menüden Mesai ve İzin → Mesaim. Next önceki sayfayı (Bugün'deki Mesaim kartı) gizli tutar; rol seçicisi gizlileri saymaz.
  const staffPage = await pageAs(staffUser);
  await staffPage.goto("/panel");
  const menu = await mainMenu(staffPage, info);
  await menu.getByRole("link", { name: "Mesai ve İzin" }).click();
  await expect(staffPage).toHaveURL(/\/panel\/mesai-izin$/);
  await expect(staffPage.getByRole("region", { name: "Mesaim" }).getByText("Mesaide değilsiniz")).toBeVisible();
  await check(staffPage, "Mesaim · mesaide değil");

  await staffPage.getByRole("button", { name: "Mesaiye başla" }).click();
  await expect(toast(staffPage, /Mesai başladı/)).toBeVisible();
  await expect(staffPage.getByRole("region", { name: "Mesaim" }).getByText("Mesaidesiniz")).toBeVisible();
  await expect(staffPage.getByText(/^Giriş \d{2}:\d{2}/)).toBeVisible();
  await check(staffPage, "Mesaim · mesaide");

  await staffPage.getByRole("button", { name: "Mesaiyi bitir" }).click();
  await expect(toast(staffPage, /Mesai bitti/)).toBeVisible();
  await expect(staffPage.getByRole("region", { name: "Mesaim" }).getByText("Mesaide değilsiniz")).toBeVisible();
  await expect(staffPage.getByRole("region", { name: "Mesaim" }).getByRole("listitem")).toHaveCount(1);
  const mine = await staffUser.get<{ staffMemberId: string }[]>("/api/time-entries");
  expect(mine.map((e) => e.staffMemberId)).toEqual([ece.id]);

  // Yönetici: gün görünümü, düzeltme, başkası adına ekleme.
  await visit(page, "/panel/mesai-izin", "Mesai · gün (yönetici)");
  const row = page.getByRole("listitem").filter({ hasText: "Ece Kalfa" });
  await row.getByRole("button", { name: /kaydını düzelt/ }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Not (isteğe bağlı)").fill("Kart unutuldu");
  await check(page, "Mesai · düzelt");
  await dialog.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Mesai düzeltildi.")).toBeVisible();
  await expect(row.getByText("· Kart unutuldu")).toBeVisible();

  const yesterday = istanbulDay(-1);
  await page.getByRole("button", { name: "Mesai ekle" }).click();
  await dialog.getByLabel("Personel").selectOption({ label: "Mehmet Usta" });
  await dialog.getByLabel("Gün").fill(yesterday);
  await dialog.getByLabel("Giriş").fill("08:00");
  await dialog.getByLabel("Çıkış").fill("18:30");
  await dialog.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Mesai eklendi.")).toBeVisible();

  // Rapor: Mehmet 10,5 saat; yasal not.
  await visit(page, `/panel/raporlar?rapor=mesai&aralik=ozel&from=${yesterday}&to=${istanbulDay()}`, "Rapor · mesai");
  const report = page.getByRole("region", { name: "Mesai raporu" });
  await expect(report.getByText(/haftalık 45 saati aşan süre fazla mesaidir/i)).toBeVisible();
  await expect(report.getByText("10,5").filter({ visible: true }).first()).toBeVisible();
  await expect(report.getByText(/Yasal bilgi amaçlıdır/)).toBeVisible();

  // Personel başkasının mesaisini görmez, Mesai sekmesi yok.
  await staffPage.goto("/panel/mesai-izin?sekme=mesai");
  await expect(staffPage.getByRole("region", { name: "Mesaim" }).getByText("Mesaide değilsiniz")).toBeVisible();
  await expect(staffPage.getByRole("link", { name: "Mesai", exact: true })).toHaveCount(0);
  await expect(staffPage.getByText("Mehmet Usta")).toHaveCount(0);
});
