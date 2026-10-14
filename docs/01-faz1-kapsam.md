# Faz 1 kapsamı (onaylandı, 10.10.2026)

Her madde "kabul ölçütü" gibi okunmalı: madde bitmiş sayılması için anlatılan davranış çalışmalı.
Parantez içindeki rakip adı, fikrin nereden alındığını gösterir.

## A. Çekirdek

- **Kayıt:** E-posta + şifre ile kayıt, e-posta doğrulama, şifremi unuttum.
- **Kurulum sihirbazı (10 dakikada kurulum):** işletme adı, logo, telefon, adres, çalışma saatleri, sektör şablonu (Hizmet / Perakende / Genel). Şablon; menüyü, örnek hizmet/ürün kategorilerini ve varsayılan ayarları belirler.
- **Ekip ve roller:** E-posta ile davet. 4 sabit rol:
  - Owner (Sahip): her şey, maaş ve abonelik dahil.
  - Manager (Yönetici): maaş ve abonelik hariç her şey.
  - Staff (Personel): takvimi görür, herkes için randevu açar ve taşır (Ayarlar'dan "sadece kendi randevularını görsün" seçilebilir; karar 2026-10-13), satış girme, müşteri görme; finans raporlarını göremez.
  - Accountant (Mali Müşavir): ücretsiz, salt okunur; raporları ve hareketleri Excel olarak indirir (KolayBi, Paraşüt).
  - Kullanıcı sayısı pakete göre sınırlı; mali müşavir sınıra sayılmaz.
- **Ortak araçlar:** uygulama içi bildirimler, Ctrl+K genel arama (müşteri, ürün, satış), aktivite günlüğü, Excel'den müşteri ve ürün içe aktarma (şablon indirilebilir).
- **KVKK:** aydınlatma metni, müşteri bazında SMS/e-posta/arama izin kaydı (tarih ve kaynakla), müşteri verisini dışa aktarma ve silme.

## B. Müşteriler

- Müşteri kartı: ad, telefon, e-posta, doğum günü, adres, etiketler, kaynak (Instagram, tavsiye, yoldan vb.), notlar.
- Zaman çizelgesi: satışlar, tahsilatlar, randevular, gönderilen mesajlar ve notlar tek akışta.
- Kayıtlı segmentler: örneğin "60 gündür gelmeyenler", "borcu olanlar", "doğum günü bu ay".
- Müşteriye bağlı hatırlatma ve görevler.

## C. Ürün/Hizmet ve Stok

- Katalog: ad, tür (ürün/hizmet), kategori, birim, KDV oranı, alış ve satış fiyatı, barkod, hizmet süresi (dakika, randevu için).
- Stok hareketleri: giriş, çıkış, sayım düzeltmesi. Satışta stok otomatik düşer; kritik seviyenin altına inince uyarı.

## D. Satış ve Teklif

- Hızlı satış ekranı (telefonda tek elle kullanılabilir): ürün/hizmet ekle, indirim, ödeme tipi (nakit, kart, havale, veresiye), müşteri seçimi isteğe bağlı.
- Teklif: logolu PDF, paylaşım linki, WhatsApp ile gönderim (wa.me linki). Müşteri linkten onaylar veya reddeder; onaylanan teklif tek tıkla satışa dönüşür.
- Faz 1'de resmi e-Fatura yok; satışlar fiş/sipariş olarak kaydedilir.

## E. Finans (ön muhasebe)

- Cari hesap: müşteri ve tedarikçi bakiyeleri, PDF ekstre.
- Kasa ve banka hesapları: tahsilat ve ödeme kaydı, hesaplar arası transfer.
- Gelir-gider: kategorili (kira, fatura, maaş, malzeme...).
- Veresiye ve vade listesi: vadesi gelen alacaklar; tek tıkla SMS veya WhatsApp hatırlatması; isteğe bağlı vade günü otomatik hatırlatma.
- **Tahsilat linki (Paraşüt):** müşteriye borç özeti ve işletmenin IBAN'ı olan bir link gider. Müşteri "Ödedim" butonuna basınca işletmeye bildirim düşer; işletme onaylayınca tahsilat kaydı oluşur. Kartla ödeme Faz 2.
- **Cari ekstre paylaşımı (Bizim Hesap):** ekstre WhatsApp, SMS veya e-postayla tek tıkla gönderilir (PDF linki).

## F. Randevu

- Takvim: personel kolonlu gün/hafta görünümü, sürükle-bırak. Hizmet süresi, personelin çalışma saatleri, izinleri ve tatiller hesaba katılır; çakışma engellenir.
- Online randevu sayfası (`/r/{slug}`): müşteri hizmet, personel (isteğe bağlı) ve boş saati seçer; telefonunu doğrular; işletme onaylar (veya otomatik onay ayarı); müşteri SMS'teki linkten en az X saat kala iptal eder. Sayfa varsayılan kapalı, Ayarlar → Randevu'dan açılır.
- Hatırlatma: randevudan 24 saat önce SMS (süre ayarlanabilir). Gelmeyen müşteri "gelmedi" olarak işaretlenir.
- Tamamlanan randevu tek tıkla satışa çevrilir.
- **Paket ve seans takibi (Salon Randevu):** "10 seans lazer" gibi seanslı paketler satılır; kalan seans her tamamlanan randevuda düşer; 1 seans kalınca uyarı. Süre bazlı sınırsız üyelik ("aylık üyelik") Faz 2'de (karar 2026-10-13, soru 10).
- **Gelmeyen müşteri uyarısı:** son 5 randevusundan 2 veya daha fazlasına gelmeyen müşterinin kartında ve yeni randevu alırken uyarı.

## G. Pazarlama (fark yaratan modül)

- Kampanya akışı: segment seç → mesaj yaz (değişkenler: {ad}, {isletme}) → önizle ve maliyet tahminini (SMS kredisi) gör → hemen gönder veya zamanla.
- Hazır otomasyonlar (aç/kapa): doğum günü mesajı; "X gündür gelmedi" geri kazanma; randevu sonrası teşekkür.
- **Memnuniyet → Google yorum:** randevudan sonra 1-5 puan sorusu linki gider. 4-5 verene işletmenin Google yorum linki gösterilir; 1-3 verenin yorumu sadece işletmeye düşer ve bildirim oluşur.
- **Boş saat kampanyası:** yarının takviminde boş aralık varsa panel öneri gösterir; tek tıkla seçilen segmente "yarın 14:00'te yer var" mesajı gider.
- **Risk listesi (Salon Randevu):** ortalama ziyaret aralığının 2 katından uzun süredir gelmeyen düzenli müşteriler otomatik listelenir.
- İYS uyumu: izni olmayan alıcılar otomatik elenir; her mesajda çıkış (RET) seçeneği.
- SMS kredisi: işletme bakiye yükler; her gönderimde düşer; bakiye azalınca uyarı.

## H. Dashboard ve Raporlar

- Bugün ekranı: bugünkü satış ve tahsilat, bugünün randevuları, vadesi gelen alacaklar, kritik stok, aktif personel.
- Raporlar: satışlar, en çok satan ürün/hizmet, alacaklar, gelir-gider, kampanya sonuçları, personel performansı. Hepsi Excel'e aktarılabilir.

## J. Personel, Mesai ve İzin

- Personel kartı: ad, görev, telefon, işe başlama, maaş (sadece Owner görür), bağlı kullanıcı hesabı (isteğe bağlı).
- Mesai: giriş-çıkış kaydı (personel kendi telefonundan veya yönetici), çalışılan saat, fazla mesai raporu.
- İzin: izin talebi → onay/ret, yıllık izin bakiyesi, izin takvimi. İzinli personel randevu takviminde kapalı görünür.
- **Prim hesabı (Salon Randevu):** hizmet ve ürün satışından yüzde veya sabit prim kuralı; ay sonu prim raporu.
- Dashboard'da "aktif personel" kartı.

## I. Abonelik ve Yönetim

- Paketler: Çırak (ücretsiz), Kalfa, Usta; paket limitleri (kullanıcı, müşteri, aylık randevu) yapılandırılabilir.
- 14 gün deneme, iyzico ile kartla tekrarlayan ödeme.
- **Şeffaf abonelik:** yenilemeden 7 gün önce SMS + e-posta hatırlatma; panelden tek tıkla iptal; abonelik bitince veriler silinmez, salt okunur erişim ve Excel dışa aktarma sürer.
- Destek: panelde WhatsApp destek butonu (wa.me) ve yardım makaleleri.
- Süper admin paneli (sadece KobiFabrika ekibi): işletmeler, abonelikler, kullanım, SMS kredisi, destek notları.

## Faz 1'de olmayanlar

`docs/00-urun-ozeti.md` → Fazlar bölümü.
