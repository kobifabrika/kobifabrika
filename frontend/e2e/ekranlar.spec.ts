import { createContact, createProduct, createQuote, createSale, line } from "./support/api";
import { test } from "./support/test";

/**
 * Ekran turu: her ekran Sahip olarak dolu veriyle açılır; otomatik kontroller (taşma, erişilebilirlik, konsol, HTTP) ve
 * her ekranın masaüstü/375 px görüntüsü rapora eklenir. Akışlar sprint dosyalarında.
 */
test("tanıtım ve giriş ekranları", async ({ page, visit }) => {
  await visit(page, "/", "Tanıtım sitesi");
  await visit(page, "/kvkk", "KVKK aydınlatma");
  await visit(page, "/giris", "Giriş");
  await visit(page, "/kayit", "Kayıt");
  await visit(page, "/sifremi-unuttum", "Şifremi unuttum");
});

test("panel ekranları (Sahip, dolu veri)", async ({ owner, ownerPage: page, visit }) => {
  const contact = await createContact(owner, { firstName: "Ayşe", lastName: "Yılmaz", email: "ayse@example.test" });
  const supplier = await createContact(owner, {
    firstName: "Toptan",
    lastName: "Gıda",
    isCustomer: false,
    isSupplier: true,
  });
  const product = await createProduct(owner, {
    name: "Pamuklu tişört",
    salePrice: 249.9,
    barcode: "8690000000011",
    criticalStockLevel: 3,
  });
  const sale = await createSale(owner, [line(product.id, 2)], [{ method: "Credit", amount: 499.8 }], contact.id);
  const quote = await createQuote(owner, contact.id, [line(product.id, 1)]);

  const screens: [string, string][] = [
    ["/panel", "Bugün"],
    ["/panel/musteriler", "Müşteriler"],
    ["/panel/musteriler/yeni", "Yeni müşteri"],
    [`/panel/musteriler/${contact.id}`, "Müşteri kartı"],
    [`/panel/musteriler/${contact.id}?sekme=cari`, "Müşteri kartı · cari"],
    [`/panel/musteriler/${contact.id}?sekme=satislar`, "Müşteri kartı · satışlar"],
    [`/panel/musteriler/${contact.id}/duzenle`, "Müşteri düzenle"],
    ["/panel/musteriler/ice-aktar", "Müşteri içe aktar"],
    ["/panel/hatirlatmalar", "Hatırlatmalar"],
    ["/panel/cari-kasa", "Cari ve kasa"],
    [`/panel/cari-kasa/tedarikciler/${supplier.id}`, "Tedarikçi kartı"],
    ["/panel/urunler", "Ürünler"],
    ["/panel/urunler/yeni", "Yeni ürün"],
    [`/panel/urunler/${product.id}`, "Ürün kartı"],
    [`/panel/urunler/${product.id}/duzenle`, "Ürün düzenle"],
    ["/panel/urunler/ice-aktar", "Ürün içe aktar"],
    ["/panel/stok", "Stok"],
    ["/panel/satislar", "Satışlar"],
    ["/panel/satislar/yeni", "Hızlı satış"],
    [`/panel/satislar/${sale.id}`, "Satış detayı"],
    [`/panel/satislar/${sale.id}/fis`, "Fiş"],
    ["/panel/teklifler", "Teklifler"],
    ["/panel/teklifler/yeni", "Yeni teklif"],
    [`/panel/teklifler/${quote.id}`, "Teklif detayı"],
    [`/panel/teklifler/${quote.id}/duzenle`, "Teklif düzenle"],
    ["/panel/ayarlar", "Ayarlar"],
    ["/panel/ayarlar/calisma-saatleri", "Çalışma saatleri"],
    ["/panel/ayarlar/ekip", "Ekip"],
    ["/panel/ayarlar/aktivite", "Aktivite günlüğü"],
    ["/panel/hesabim", "Hesabım"],
  ];
  for (const [url, name] of screens) await visit(page, url, name);
});
