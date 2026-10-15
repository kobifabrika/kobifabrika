import { createContact, createProduct, createQuote, createSale, line } from "./support/api";
import { book, createService, createStaff, pastSlot } from "./support/appointments";
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
  const accounts = await owner.get<{ id: string }[]>("/api/cash-accounts");
  const service = await createService(owner, "Saç kesimi", 45);
  const staff = await createStaff(owner, "Zeynep Usta");
  const slot = pastSlot(90);
  const appointment = await book(owner, { contactId: contact.id, staffId: staff.id, serviceId: service.id, startAt: slot.iso });

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
    ["/panel/ayarlar/finans", "Finans ayarları"],
    ["/panel/cari-kasa?sekme=vadeler", "Vadeler"],
    ["/panel/cari-kasa?sekme=kasa", "Kasa ve banka"],
    [`/panel/cari-kasa/hesaplar/${accounts[0].id}`, "Hesap kartı"],
    ["/panel/cari-kasa?sekme=gelir-gider", "Gelir-gider"],
    ["/panel/cari-kasa/gelir-gider/yeni?tur=gider", "Gider ekle"],
    ["/panel/raporlar", "Rapor · satışlar"],
    ["/panel/raporlar?rapor=urunler", "Rapor · en çok satan"],
    ["/panel/raporlar?rapor=alacaklar", "Rapor · alacaklar"],
    ["/panel/raporlar?rapor=gelir-gider", "Rapor · gelir-gider"],
    ["/panel/ayarlar/ekip", "Ekip"],
    ["/panel/ayarlar/aktivite", "Aktivite günlüğü"],
    ["/panel/hesabim", "Hesabım"],
    [`/panel/randevular?tarih=${slot.day}`, "Takvim · gün"],
    [`/panel/randevular?gorunum=hafta&tarih=${slot.day}`, "Takvim · hafta"],
    [`/panel/randevular?tarih=${slot.day}&randevu=${appointment.id}`, "Randevu ayrıntısı"],
    [`/panel/musteriler/${contact.id}?sekme=randevular`, "Müşteri kartı · randevular"],
    ["/panel/personel", "Personel"],
    ["/panel/personel/yeni", "Personel ekle"],
    [`/panel/personel/${staff.id}`, "Personel kartı"],
    [`/panel/personel/${staff.id}?sekme=saatler`, "Personel · çalışma saatleri"],
    [`/panel/personel/${staff.id}?sekme=hizmetler`, "Personel · hizmetler"],
    [`/panel/personel/${staff.id}?sekme=kapali`, "Personel · kapalı zamanlar"],
    ["/panel/ayarlar/randevu", "Randevu ayarları"],
    ["/panel/urunler?sekme=paketler", "Paketler"],
    ["/panel/urunler/paketler/yeni", "Yeni paket"],
    [`/panel/musteriler/${contact.id}?sekme=paketler`, "Müşteri kartı · paketler"],
    [`/panel/personel/${staff.id}?sekme=izin`, "Personel · izin bakiyesi"],
    [`/panel/personel/${staff.id}?sekme=maas`, "Personel · maaş"],
    ["/panel/mesai-izin", "Mesai ve İzin · mesai"],
    ["/panel/mesai-izin?sekme=izinler", "Mesai ve İzin · izinler"],
    ["/panel/mesai-izin?sekme=takvim", "Mesai ve İzin · izin takvimi"],
    ["/panel/mesai-izin/izin-talebi", "İzin gir"],
    ["/panel/raporlar?rapor=mesai", "Rapor · mesai"],
    ["/panel/ayarlar/personel", "Personel ayarları"],
    ["/panel/personel?sekme=prim", "Personel · prim kuralları"],
    ["/panel/raporlar?rapor=prim", "Rapor · prim"],
    ["/panel/raporlar?rapor=personel", "Rapor · personel"],
    ["/panel/raporlar?rapor=kampanyalar", "Rapor · kampanyalar"],
    ["/panel/raporlar?rapor=anket", "Rapor · anket"],
  ];
  for (const [url, name] of screens) await visit(page, url, name);
});
