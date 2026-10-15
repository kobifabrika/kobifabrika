import { addMember } from "./support/api";
import { createStaff, openAllDay, workAllDay } from "./support/appointments";
import { istanbulDay } from "./support/finance";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 7B Bugün: yöneticide "Aktif personel" kartı (mesaidekiler "…'den beri", bugün izinliler tür dahil); Personel rolünde
 * "Mesaim" kartı ve tek düğme (başkalarının mesaisi ve izin türü görünmez).
 */
test("Bugün: aktif personel kartı ve personelin Mesaim kartı", async ({ owner, ownerPage: page, visit, check, pageAs }) => {
  await openAllDay(owner);
  const staffUser = await addMember(owner, "Staff", "Personel Ece");
  const { user } = await staffUser.get<{ user: { id: string } }>("/api/auth/me");
  await createStaff(owner, "Ece Kalfa", user.id);
  const mehmet = await createStaff(owner, "Mehmet Usta");
  await workAllDay(owner, mehmet.id);

  const types = await owner.get<{ id: string; isAnnual: boolean }[]>("/api/leave-types");
  const today = istanbulDay();
  await owner.post("/api/leave-requests", {
    staffMemberId: mehmet.id,
    leaveTypeId: types.find((t) => t.isAnnual)!.id,
    startDate: today,
    endDate: today,
    halfDay: "None",
    note: null,
  });

  // Personel: Bugün'de Mesaim kartı; tek düğmeyle mesaiye başlar.
  const staffPage = await pageAs(staffUser);
  await visit(staffPage, "/panel", "Bugün · Personel (mesaide değil)");
  const myCard = staffPage.getByRole("region", { name: "Mesaim" });
  await expect(myCard.getByText("Mesaide değilsiniz")).toBeVisible();
  await myCard.getByRole("button", { name: "Mesaiye başla" }).click();
  await expect(toast(staffPage, /Mesai başladı/)).toBeVisible();
  await expect(myCard.getByRole("button", { name: "Mesaiyi bitir" })).toBeVisible();
  await check(staffPage, "Bugün · Personel (mesaide)");
  await expect(staffPage.getByText("Aktif personel")).toHaveCount(0);
  await expect(staffPage.getByText("Mehmet Usta")).toHaveCount(0);

  // Sahip: Aktif personel kartı.
  await visit(page, "/panel", "Bugün · aktif personel");
  const card = page.getByRole("list", { name: "Mesaidekiler" });
  await expect(card.getByText("Ece Kalfa")).toBeVisible();
  await expect(card.getByText(/^\d{2}:\d{2}'den beri$/)).toBeVisible();
  const leave = page.getByRole("list", { name: "Bugün izinli" });
  await expect(leave.getByText("Mehmet Usta")).toBeVisible();
  await expect(leave.getByText("Yıllık izin")).toBeVisible();
  await page.getByRole("link", { name: "Mesai ve izin", exact: true }).click();
  await expect(page).toHaveURL(/\/panel\/mesai-izin$/);
});
