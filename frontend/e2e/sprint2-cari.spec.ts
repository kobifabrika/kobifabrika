import { addMember, createContact } from "./support/api";
import { toast } from "./support/nav";
import { expect, test, type Guard } from "./support/test";
import type { Page } from "@playwright/test";

/** Kartın "Güncel bakiye" satırı. */
const balance = (page: Page) => page.getByText("Güncel bakiye").locator("..");

async function cashBalance(page: Page, name: "Nakit Kasa" | "Banka") {
  await page.goto("/panel/cari-kasa?sekme=kasa");
  return page.getByRole("listitem").filter({ hasText: name }).first();
}

/** Sprint 2: açılış, borç/alacak, tahsilat al, geri al; bakiyeler ve Nakit Kasa; PDF ekstre ve ekstre linki. */
test("cari hareketler, tahsilat, geri alma ve ekstre", async ({ ownerPage: page, newPublicPage, visit, check }) => {
  // Açılış bakiyesi müşteri formundan.
  await page.goto("/panel/musteriler/yeni");
  await page.getByLabel("Ad", { exact: true }).fill("Mehmet");
  await page.getByLabel("Soyad").fill("Öz");
  await page.getByLabel("Tutar (TL)").fill("1000");
  await page.getByLabel("Kim kime borçlu?").selectOption("Receivable");
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(page).toHaveURL(/\/panel\/musteriler\/[0-9a-f-]{36}/);
  const cardUrl = page.url().split("?")[0];

  await visit(page, `${cardUrl}?sekme=cari`, "Müşteri kartı · cari");
  await expect(balance(page)).toContainText("Alacak 1.000,00 TL");

  // Borç 250 → 1.250; alacak 50 → 1.200
  await page.getByRole("button", { name: "Borç / alacak" }).click();
  await page.getByRole("dialog").getByLabel("Tutar (TL)").fill("250");
  await page.getByRole("dialog").getByLabel("Açıklama").fill("Saç boyası");
  await check(page, "Borç / alacak ekle");
  await page.getByRole("dialog").getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Hareket kaydedildi.")).toBeVisible();
  await expect(balance(page)).toContainText("Alacak 1.250,00 TL");

  await page.getByRole("button", { name: "Borç / alacak" }).click();
  await page.getByRole("dialog").getByText("Alacak", { exact: true }).click();
  await page.getByRole("dialog").getByLabel("Tutar (TL)").fill("50");
  await page.getByRole("dialog").getByLabel("Açıklama").fill("İade");
  await page.getByRole("dialog").getByRole("button", { name: "Kaydet" }).click();
  await expect(balance(page)).toContainText("Alacak 1.200,00 TL");

  // Tahsilat 700 Nakit Kasa'ya → 500; kasa 700
  await page.getByRole("button", { name: "Tahsilat al" }).click();
  await page.getByRole("dialog").getByLabel("Alınan tutar (TL)").fill("700");
  await expect(page.getByRole("dialog").getByLabel("Nereye girdi?")).toHaveValue(/.+/);
  await check(page, "Tahsilat al");
  await page.getByRole("dialog").getByRole("button", { name: "Tahsilatı kaydet" }).click();
  await expect(toast(page, /Tahsilat kaydedildi/)).toBeVisible();
  await expect(balance(page)).toContainText("Alacak 500,00 TL");
  await expect(await cashBalance(page, "Nakit Kasa")).toContainText("700,00 TL");
  await check(page, "Kasa ve banka");

  // Tahsilatı geri al → 1.200; kasa 0
  await page.goto(`${cardUrl}?sekme=cari`);
  page.once("dialog", (d) => d.accept());
  await page.getByRole("button", { name: "Tahsilatı geri al" }).click();
  await expect(toast(page, "Tahsilat geri alındı.")).toBeVisible();
  await expect(balance(page)).toContainText("Alacak 1.200,00 TL");
  await expect(await cashBalance(page, "Nakit Kasa")).toContainText("0,00 TL");

  // Bakiyeler listesi
  await visit(page, "/panel/cari-kasa", "Cari ve Kasa · bakiyeler");
  await expect(page.getByText("Mehmet Öz").filter({ visible: true }).first()).toBeVisible();

  // PDF ekstre
  await page.goto(`${cardUrl}?sekme=cari`);
  await page.getByRole("button", { name: "Ekstre" }).click();
  await check(page, "Cari ekstre");
  const pdfHref = await page.getByRole("link", { name: "PDF'i aç / indir" }).getAttribute("href");
  const pdf = await page.request.get(pdfHref!);
  expect(pdf.status()).toBe(200);
  expect(pdf.headers()["content-type"]).toContain("application/pdf");
  expect((await pdf.body()).subarray(0, 4).toString()).toBe("%PDF");

  // Ekstre linki: giriş yapılmamış ayrı bağlamda açılır.
  await page.getByRole("button", { name: "Paylaşım linki oluştur" }).click();
  const url = (await page.getByRole("dialog").getByText(/\/ekstre\//).textContent())!.trim();
  const customer = await newPublicPage();
  await visit(customer, url, "Herkese açık ekstre");
  await expect(customer.getByRole("heading", { name: "Cari hesap ekstresi" })).toBeVisible();
  await expect(customer.getByText("Saç boyası")).toBeVisible();
  await expect(customer.getByText(/1\.200,00/).first()).toBeVisible();
});

test("Personel bakiyeleri göremez", async ({ owner, pageAs, guard }) => {
  const contact = await createContact(owner, { firstName: "Gizli", lastName: "Bakiye" });
  await owner.post(`/api/contacts/${contact.id}/ledger`, {
    kind: "Debit",
    amount: 300,
    date: null,
    dueDate: null,
    description: "Veresiye",
  });
  const staff = await pageAs(await addMember(owner, "Staff"));

  await staff.goto("/panel/cari-kasa");
  await expect(staff.getByText("Bu sayfayı görme yetkin yok")).toBeVisible();

  await staff.goto(`/panel/musteriler/${contact.id}`);
  await expect(staff.getByRole("heading", { name: "Gizli Bakiye" })).toBeVisible();
  await expect(staff.getByText(/300,00/)).toHaveCount(0);
  await expect(staff.getByRole("link", { name: "Cari" })).toHaveCount(0);

  await staff.goto("/panel/musteriler");
  await expect(staff.getByText("Gizli Bakiye").filter({ visible: true }).first()).toBeVisible();
  await expect(staff.getByText(/300,00/)).toHaveCount(0);

  allowForbidden(guard);
  const res = await staff.request.get(`http://localhost:5180/api/contacts/${contact.id}/ledger`);
  expect(res.status()).toBe(403);
});

function allowForbidden(guard: Guard) {
  guard.allow(403, /\/ledger/);
}
