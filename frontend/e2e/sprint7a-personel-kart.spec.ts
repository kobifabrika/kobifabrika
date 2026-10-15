import { addMember } from "./support/api";
import { createStaff } from "./support/appointments";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 7A personel kartı: işe başlama, doğum tarihi, not; maaş sekmesi Sahip'te var (kaydet, "Maaş öde" → ödenen maaşlar),
 * Yönetici'de yok ve API maaşı hiç döndürmez; izin sekmesinde açılış bakiyesi ve yasal not; Ayarlar → Personel (fazla mesai eşiği,
 * izin türü ekleme).
 */
test("personel kartı: bilgiler, maaş, ödenen maaşlar, izin bakiyesi ve personel ayarları", async ({ owner, ownerPage: page, visit, check, pageAs }) => {
  const staff = await createStaff(owner, "Zeynep Usta");

  // Bilgiler.
  await visit(page, `/panel/personel/${staff.id}`, "Personel kartı · bilgiler (7A)");
  await page.getByLabel("İşe başlama (isteğe bağlı)").fill("2021-03-01");
  await page.getByLabel("Doğum tarihi (isteğe bağlı)").fill("1994-06-15");
  await page.getByLabel("Not (isteğe bağlı)").fill("Cumartesi öğleden sonra çalışmıyor");
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(toast(page, "Personel kaydedildi.")).toBeVisible();
  const saved = await owner.get<{ startDate: string; birthDate: string }>(`/api/staff/${staff.id}`);
  expect(saved.startDate).toBe("2021-03-01");
  expect(saved.birthDate).toBe("1994-06-15");

  // Maaş: kaydet ve öde.
  await page.getByRole("link", { name: "Maaş", exact: true }).click();
  await expect(page).toHaveURL(/sekme=maas/);
  await page.getByLabel("Tutar (aylık, boş: girilmemiş)").fill("32.500");
  await page.getByRole("radio", { name: "Net" }).click();
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(toast(page, "Maaş kaydedildi.")).toBeVisible();
  await expect(page.getByText(/adına kayıtlı maaş ödemesi yok/)).toBeVisible();
  await check(page, "Personel kartı · maaş");

  await page.getByRole("link", { name: "Maaş öde" }).click();
  await expect(page).toHaveURL(/gelir-gider\/yeni\?tur=gider&personel=/);
  await expect(page.getByRole("radio", { name: "Maaş" })).toBeChecked();
  await expect(page.getByLabel("Personel (isteğe bağlı)")).toHaveValue(staff.id);
  await page.getByLabel("Tutar (TL)").fill("30000");
  await page.getByLabel("Açıklama (isteğe bağlı)").fill("Ekim maaşı");
  await check(page, "Gider ekle · maaş (personel seçili)");
  await page.getByRole("button", { name: "Kaydet", exact: true }).click();
  await expect(toast(page, "Gider kaydedildi.")).toBeVisible();

  await visit(page, `/panel/personel/${staff.id}?sekme=maas`, "Personel kartı · ödenen maaşlar");
  await expect(page.getByText("Ekim maaşı")).toBeVisible();
  await expect(page.getByText("Toplam 30.000,00 TL")).toBeVisible();

  // İzin bakiyesi: açılış bakiyesi.
  await page.getByRole("link", { name: "İzin", exact: true }).click();
  await expect(page.getByTestId("leave-balance")).toHaveText("0 gün");
  await expect(page.getByText(/Yasal bilgi amaçlıdır/)).toBeVisible();
  await expect(page.getByText(/bu yılki hak: 14 gün/)).toBeVisible();
  await page.getByRole("button", { name: "Bakiyeyi düzelt" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Gün").fill("6,5");
  await check(page, "İzin bakiyesi · düzelt");
  await dialog.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Bakiye güncellendi.")).toBeVisible();
  await expect(page.getByTestId("leave-balance")).toHaveText("6,5 gün");
  await check(page, "Personel kartı · izin");

  // Yönetici: maaş sekmesi yok, API maaşı döndürmez.
  const manager = await addMember(owner, "Manager", "Yönetici Mert");
  const raw = JSON.stringify(await manager.get(`/api/staff/${staff.id}`));
  expect(raw).not.toMatch(/salary/i);
  expect(raw).not.toContain("32500");
  const managerPage = await pageAs(manager);
  await managerPage.goto(`/panel/personel/${staff.id}?sekme=maas`);
  await expect(managerPage.getByRole("link", { name: "İzin", exact: true })).toBeVisible();
  await expect(managerPage.getByRole("link", { name: "Maaş", exact: true })).toHaveCount(0);
  await expect(managerPage.getByLabel("Ad soyad")).toHaveValue("Zeynep Usta"); // sekme yoksa Bilgiler
  await check(managerPage, "Personel kartı · Yönetici (maaş yok)");

  // Ayarlar → Personel.
  await visit(page, "/panel/ayarlar/personel", "Ayarlar · personel");
  await expect(page.getByLabel("Haftalık eşik (saat)")).toHaveValue("45");
  await page.getByLabel("Haftalık eşik (saat)").fill("40");
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Fazla mesai eşiği kaydedildi.")).toBeVisible();
  expect((await owner.get<{ overtimeWeeklyHours: number }>("/api/business/staff-settings")).overtimeWeeklyHours).toBe(40);
  await expect(page.getByText("Yıllık izin", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Tür ekle" }).click();
  await page.getByRole("dialog").getByLabel("Ad").fill("Babalık izni");
  await check(page, "Ayarlar · izin türü ekle");
  await page.getByRole("dialog").getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "İzin türü eklendi.")).toBeVisible();
  await expect(page.getByText("Babalık izni")).toBeVisible();
});
