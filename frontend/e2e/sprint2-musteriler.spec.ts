import path from "node:path";

import { createContact, createSale, line, createProduct } from "./support/api";
import { toast } from "./support/nav";
import { expect, test } from "./support/test";

const fixtures = path.join(__dirname, "fixtures");

/** Sprint 2: müşteri ekle/düzenle, etiket, not, KVKK izni, segment, hatırlatma, Excel'den içe aktarma. */
test("müşteri kartı: ekle, etiket, KVKK, düzenle, not, hatırlatma", async ({ ownerPage: page, visit, check }) => {
  await visit(page, "/panel/musteriler/yeni", "Yeni müşteri");
  await page.getByLabel("Ad", { exact: true }).fill("Ayşe");
  await page.getByLabel("Soyad").fill("Yılmaz");
  await page.getByRole("textbox", { name: "Telefon" }).fill("0532 123 45 67");
  await page.getByRole("textbox", { name: "E-posta" }).fill("ayse@example.test");
  await page.getByLabel("Yeni etiket adı").fill("VIP");
  await page.getByRole("button", { name: "Ekle", exact: true }).click();
  await expect(page.getByRole("button", { name: "VIP", pressed: true })).toBeVisible();
  await page.getByRole("switch", { name: "SMS ile kampanya ve bilgilendirme" }).click();
  await check(page, "Yeni müşteri · dolu form");
  await page.getByRole("button", { name: "Kaydet" }).click();

  await expect(toast(page, "Ayşe Yılmaz eklendi.")).toBeVisible();
  await expect(page).toHaveURL(/\/panel\/musteriler\/[0-9a-f-]{36}/);
  await expect(page.getByRole("heading", { name: "Ayşe Yılmaz" })).toBeVisible();
  // KVKK: SMS izni verildi (tarih ve kaynakla), diğerleri yok.
  await expect(page.getByText("✓ İzin var")).toHaveCount(1);
  await expect(page.getByText("İzin yok")).toHaveCount(2);
  await check(page, "Müşteri kartı · özet");
  const contactUrl = page.url().split("?")[0];

  // Düzenle: soyad ve adres.
  await page.goto(`${contactUrl}/duzenle`);
  await page.getByLabel("Soyad").fill("Yılmaz Kaya");
  await page.getByLabel("Adres").fill("Moda Cad. No: 5");
  await page.getByRole("button", { name: "Kaydet" }).click();
  await expect(toast(page, "Kart güncellendi.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Ayşe Yılmaz Kaya" })).toBeVisible();
  await expect(page.getByText("Moda Cad. No: 5")).toBeVisible();

  // Not
  await page.goto(`${contactUrl}?sekme=notlar`);
  await page.getByLabel("Yeni not").fill("Kuru cilt; hassas ürün öner.");
  await page.getByRole("button", { name: "Notu ekle" }).click();
  await expect(page.getByText("Kuru cilt; hassas ürün öner.")).toBeVisible();
  await check(page, "Müşteri kartı · notlar");

  // Hatırlatma
  await page.goto(`${contactUrl}?sekme=hatirlatmalar`);
  await page.getByRole("button", { name: "Hatırlatma ekle" }).click();
  await page.getByLabel("Ne hatırlatılsın?").fill("Yeni sezon ürünlerini haber ver");
  await check(page, "Hatırlatma ekle");
  await page.getByRole("dialog").getByRole("button", { name: /Kaydet|Ekle/ }).last().click();
  await expect(toast(page, "Hatırlatma eklendi.")).toBeVisible();
  await expect(page.getByText("Yeni sezon ürünlerini haber ver").first()).toBeVisible();
  await visit(page, "/panel/hatirlatmalar", "Hatırlatmalar");
  await expect(page.getByText("Yeni sezon ürünlerini haber ver").first()).toBeVisible();

  // Zaman çizelgesi: kayıt, not, hatırlatma, izin olayları
  await page.goto(`${contactUrl}?sekme=zaman`);
  await expect(page.getByText(/Kuru cilt/).first()).toBeVisible();
  await check(page, "Müşteri kartı · zaman çizelgesi");
});

test("segment: etikete göre süzüp segment olarak kaydet", async ({ owner, ownerPage: page, visit }) => {
  const vip = await owner.post<{ id: string }>("/api/tags", { name: "VIP", color: "Gray" });
  await createContact(owner, { firstName: "Ali", lastName: "Veli", tagIds: [vip.id] });
  await createContact(owner, { firstName: "Fatma", lastName: "Kara" });

  await visit(page, "/panel/musteriler", "Müşteriler");
  await expect(page.getByText("Fatma Kara").filter({ visible: true })).toBeVisible();
  // Filtre: VIP etiketi → sadece Ali.
  await page.getByRole("button", { name: "Filtre" }).click();
  await page.getByRole("dialog").getByLabel("VIP").check();
  await page.getByRole("button", { name: "Uygula" }).click();
  await expect(page.getByText("Ali Veli").filter({ visible: true })).toBeVisible();
  await expect(page.getByText("Fatma Kara").filter({ visible: true })).toHaveCount(0);

  // Aynı süzgeç segment olarak kaydedilir ve uygulanır.
  await page.getByRole("button", { name: "Filtre" }).click();
  await page.getByLabel("Segment olarak kaydet").fill("VIP müşteriler");
  await page.getByRole("button", { name: "Segmenti kaydet" }).click();
  await expect(toast(page, '"VIP müşteriler" segmenti kaydedildi.')).toBeVisible();
  await expect(page.getByRole("button", { name: "VIP müşteriler", pressed: true })).toBeVisible();
  await expect(page.getByText("Fatma Kara").filter({ visible: true })).toHaveCount(0);

  await page.getByRole("group", { name: "Segmentler" }).getByRole("button", { name: "Tümü" }).click();
  await expect(page.getByText("Fatma Kara").filter({ visible: true })).toBeVisible();
  await page.getByRole("group", { name: "Segmentler" }).getByRole("button", { name: "VIP müşteriler" }).click();
  await expect(page.getByText("Fatma Kara").filter({ visible: true })).toHaveCount(0);
  await expect(page.getByText("Ali Veli").filter({ visible: true })).toBeVisible();
});

test("Excel'den müşteri içe aktarma: tekrarlar atlanır", async ({ owner, ownerPage: page, visit, check }) => {
  // Dosyadaki 4. satır mevcut müşteriyle aynı telefon; 5. satır dosya içinde tekrar.
  await createContact(owner, { firstName: "Elif", lastName: "Su", phone: "0532 100 00 04" });

  await visit(page, "/panel/musteriler/ice-aktar", "Müşteri içe aktar");
  await page.locator('input[type="file"]').setInputFiles(path.join(fixtures, "musteriler.xlsx"));
  await expect(page.getByText("musteriler.xlsx")).toBeVisible();
  await expect(page.getByText("· 5 satır")).toBeVisible();
  await check(page, "İçe aktar · sütun eşleme");
  await page.getByRole("button", { name: "Önizle" }).click();
  await expect(page.getByRole("button", { name: "3 kaydı aktar" })).toBeVisible();
  await check(page, "İçe aktar · önizleme");
  await page.getByRole("button", { name: "3 kaydı aktar" }).click();
  await expect(page.getByText("3 kayıt eklendi.")).toBeVisible();
  await expect(page.getByText(/2 satır .* nedeniyle atlandı/)).toBeVisible();
  await check(page, "İçe aktar · rapor");

  const list = await owner.get<{ items: { displayName: string }[] }>("/api/contacts?pageSize=50");
  expect(list.items.map((c) => c.displayName).sort()).toEqual(["Ali Veli", "Elif Su", "Fatma Kara", "Hasan Demir"]);
});

test("satış yapan müşterinin son ziyareti kartta görünür", async ({ owner, ownerPage: page }) => {
  const contact = await createContact(owner, { firstName: "Zeki", lastName: "Müren" });
  const product = await createProduct(owner, { name: "Şampuan", salePrice: 100 });
  await createSale(owner, [line(product.id)], [{ method: "Cash", amount: 100 }], contact.id);
  await page.goto(`/panel/musteriler/${contact.id}`);
  await expect(page.getByText("Son ziyaret").locator("..")).not.toContainText("—");
});
