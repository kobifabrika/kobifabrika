import { createContact, uniquePhone } from "./support/api";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

/**
 * Sprint 6A KVKK: müşteri kartında "Verilerini dışa aktar" (Excel/JSON) ve "Kişisel verilerini sil" (sadece Sahip; geri alınamaz
 * uyarısı ve ad yazarak onay) → kart "Silinmiş müşteri #1", kişisel veriler yok, cari tutar duruyor.
 */
test("KVKK: verileri dışa aktar ve kişisel verileri sil", async ({ owner, ownerPage: page, visit, check }) => {
  const contact = await createContact(owner, {
    firstName: "Selin",
    lastName: "Demir",
    phone: uniquePhone(),
    email: "selin@ornek.test",
    notes: "Alerjisi var",
    openingBalance: { amount: 250, direction: "Receivable" },
  });

  await visit(page, `/panel/musteriler/${contact.id}`, "Müşteri kartı · KVKK öncesi");
  await page.getByRole("button", { name: "Diğer işlemler" }).click();
  const excel = page.getByRole("menuitem", { name: "Verilerini dışa aktar (Excel)" });
  await expect(excel).toHaveAttribute("href", new RegExp(`/api/contacts/${contact.id}/personal-data\\?format=xlsx$`));
  await expect(page.getByRole("menuitem", { name: "Verilerini dışa aktar (JSON)" })).toBeVisible();
  const json = await owner.get<{ contact: { email: string; notes: string } }>(`/api/contacts/${contact.id}/personal-data`);
  expect(json.contact).toMatchObject({ email: "selin@ornek.test", notes: "Alerjisi var" });

  await page.getByRole("menuitem", { name: "Kişisel verilerini sil" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("Bu işlem geri alınamaz.")).toBeVisible();
  const confirm = dialog.getByRole("button", { name: "Kişisel verilerini sil" });
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel(/Onaylamak için müşterinin adını yazın/).fill("Selin Demir");
  await check(page, "Müşteri kartı · kişisel verileri sil");
  await confirm.click();
  await expect(toast(page, /Kişisel veriler silindi/)).toBeVisible();

  await expect(page.getByRole("heading", { name: "Silinmiş müşteri #1" })).toBeVisible();
  await expect(page.getByText(/kişisel verileri .* tarihinde silindi/)).toBeVisible();
  await expect(page.getByText("selin@ornek.test")).toHaveCount(0);
  await expect(page.getByText("Alerjisi var")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Diğer işlemler" })).toHaveCount(0);
  await check(page, "Müşteri kartı · anonimleştirildi");

  const after = await owner.get<{ phone: string | null; balance: number; isAnonymized: boolean }>(`/api/contacts/${contact.id}`);
  expect(after).toMatchObject({ phone: null, balance: 250, isAnonymized: true });
});
