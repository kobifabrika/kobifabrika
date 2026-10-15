# Faz 1 kapsamı (onaylandı, 10.10.2026)

Her madde "kabul ölçütü" gibi okunmalı: madde bitmiş sayılması için anlatılan davranış çalışmalı.
Parantez içindeki rakip adı, fikrin nereden alındığını gösterir.

## A. Çekirdek

- **Kayıt:** E-posta + şifre ile kayıt, e-posta doğrulama, şifremi unuttum.
- **Kurulum sihirbazı (10 dakikada kurulum):** işletme adı, logo, telefon, adres, çalışma saatleri, sektör şablonu (Hizmet / Perakende / Genel). Şablon; menüyü, örnek hizmet/ürün kategorilerini ve varsayılan ayarları belirler.
- **Ekip ve roller:** E-posta ile davet. 4 sabit rol:
  - Owner (Sahip): her şey, maaş ve abonelik dahil.
  - Manager (Yönetici): maaş ve abonelik hariç her şey (maaş giderini "Personel gideri" olarak tutarla görür; personel kartında maaş yok).
  - Staff (Personel): takvimi görür, herkes için randevu açar ve taşır (Ayarlar'dan "sadece kendi randevularını görsün" seçilebilir; karar 2026-10-13), satış girme (satır personelini başkası seçebilir; varsayılandan farklı seçim aktivite günlüğüne yazılır), müşteri görme; finans raporlarını ve başkalarının primini göremez, sadece kendi primini "Primim"de görür (karar 2026-10-15 b).
  - Accountant (Mali Müşavir): ücretsiz, salt okunur; raporları ve hareketleri Excel olarak indirir (KolayBi, Paraşüt); bordro için personel maaşını salt okunur görür (karar 2026-10-15 a).
  - Kullanıcı sayısı pakete göre sınırlı; mali müşavir sınıra sayılmaz.
- **Ortak araçlar:** uygulama içi bildirimler, Ctrl+K genel arama (müşteri, ürün, satış), aktivite günlüğü, Excel'den müşteri ve ürün içe aktarma (şablon indirilebilir).
- **KVKK:** aydınlatma metni, müşteri bazında SMS/e-posta/arama izin kaydı (tarih ve kaynakla), müşteri verisini dışa aktarma (Excel/JSON; Sahip, Yönetici) ve silme (anonimleştirme; sadece Sahip, geri alınamaz; satış ve cari tutarlar kalır, kart "Silinmiş müşteri #N"; tüzel kişi kaydı anonimleştirilmez) (Sprint 6A).

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

- Kampanya akışı: segment seç → mesaj yaz (değişkenler: {ad}, {isletme}) → önizle ve maliyet tahminini (SMS kredisi) gör → hemen gönder veya zamanla. Sadece SMS (e-posta kampanyası Faz 1'de yok); pazarlama SMS'i 09:00-21:00 arası, aynı müşteriye günde en fazla 1 (Sprint 6A).
- Hazır otomasyonlar (aç/kapa, varsayılan kapalı; şablon, saat/gecikme düzenlenir; son 30 gün SMS ve kredi): doğum günü mesajı (29 Şubat doğumlular artık olmayan yılda 28 Şubat'ta); "X gündür gelmedi" geri kazanma (aynı kişiye en fazla Y günde bir); randevu sonrası teşekkür; vade günü hatırlatması (bilgilendirme, bir kez) (Sprint 6B).
- **Memnuniyet → Google yorum:** randevudan sonra (gecikme ayarlanabilir) 1-5 puan linki gider (`/a/kod`, 10 karakter, 7 gün, tek kullanım; sadece SMS izni olana; teşekkürle birlikte açıksa tek SMS). Google yorum linki herkese gösterilir (Google "review gating"i yasaklar): 1-3'te "Bize iletin" yorum kutusu önde (yorum sadece işletmeye, bildirim), 4-5'te Google düğmesi önde (Sprint 6B).
- **Boş saat kampanyası:** yarının takviminde boş aralık varsa (en kısa hizmet süresinden uzun; personel başına en fazla 2, toplam 3) Bugün ekranında ve takvimin yarın görünümünde öneri; tek tıkla hazır mesajla kampanya sihirbazı (Sprint 6B).
- **Risk listesi (Salon Randevu):** en az 3 ziyareti olan ve son ziyaretten bu yana max(2 × ortalama aralık, 14 gün)'den fazla geçen müşteriler; Müşteriler'de "Kaybedilme riski" segmenti, Bugün'de kart, listeden kampanya. Ziyaret = tamamlanan satış veya randevu; aynı gün ikisi tek ziyaret (Sprint 6B).
- İYS uyumu: izni olmayan ve çıkış yapmış alıcılar otomatik elenir; her pazarlama mesajında kısa çıkış linki (tek ekranda "SMS'leri durdur") ve RET. Gerçek İYS bildirimi gerçek SMS sağlayıcısıyla pilot öncesi (Sprint 6A'da arayüz ve sahte istemci).
- SMS kredisi: her ücretli SMS (bilgilendirme dahil; doğrulama kodu hariç) segment kadar düşer, eksiye düşmez; kredi bitince SMS gitmez ve işletmeye bildirim; bakiye azalınca uyarı (eşik Ayarlar → SMS). Yeni işletmeye bir kez 50 kredi. Kredi yüklemeyi Sprint 8'e (iyzico) kadar platform yapar; işletme destekten ister (Sprint 6A).

## H. Dashboard ve Raporlar

- Bugün ekranı: bugünkü satış ve tahsilat, bugünün randevuları, vadesi gelen alacaklar, kritik stok, aktif personel (mesaidekiler, bugün izinliler, unutulan çıkış; Personel rolünde "Mesaim" kartı ve tek düğme) (Sprint 7B).
- Raporlar: satışlar, en çok satan ürün/hizmet, alacaklar, gelir-gider, mesai, prim (ay), personel performansı (satış, hizmet, randevu, gelmedi oranı, anket ortalaması, prim, çalışılan saat), kampanya sonuçları (gönderilen, nedenine göre elenen, kredi, sonradan çıkış yapan), memnuniyet anketi. Hepsi Excel'e aktarılabilir; maaş hiçbir raporda yok (Sprint 7B).

## J. Personel, Mesai ve İzin

- Personel kartı: ad, görev, telefon, işe başlama, isteğe bağlı doğum tarihi (izin hakkı için), not, maaş (aylık, brüt/net; Sahip düzenler, Mali Müşavir salt okunur görür, Yönetici ve Personel göremez; API alanı hiç döndürmez), bağlı kullanıcı hesabı (isteğe bağlı). TC kimlik no saklanmaz. Maaş giderine isteğe bağlı personel; kartta "Ödenen maaşlar" (Sprint 7A).
- Mesai: personel kendi telefonundan tek düğmeyle giriş-çıkış; yönetici başkası adına ekler ve düzeltir (eski/yeni saat aktivite günlüğünde); açık mesaiye ikinci giriş olmaz. Çıkışı unutulan mesai işletme kapanışından 2 saat sonra (kapalı günde girişten 12 saat sonra) "çıkış unutuldu" işaretlenir, süresi sayılmaz, yönetici çıkışı girer. Çalışılan saat ve fazla mesai raporu: haftalık eşiği (varsayılan 45, Ayarlar → Personel) aşan süre; gün ve hafta İstanbul saatiyle, gece yarısını aşan mesai bölünür. Konum doğrulaması Faz 2 (Sprint 7A).
- İzin: türler (Yıllık izin, Mazeret izni, Ücretsiz izin, Hastalık/rapor; eklenir/düzenlenir; sağlık bilgisi tutulmaz), talep → onay/ret (bildirim iki yönlü), yarım gün (tek günlük izinde), gün sayımında kapalı gün, tatil ve personelin çalışmadığı gün sayılmaz. Yıllık izin bakiyesi: İş Kanunu 53'e göre öneri (1-5 yıl 14, 5-15 yıl 20, 15+ yıl 26 gün; 18 ve altı, 50 ve üstü en az 20), yıl dönümünde otomatik eklenir, açılış/düzeltme elle; eksi bakiye (avans izin) engellenmez; "yasal bilgi amaçlıdır" notu. İzin takvimi (ay görünümü; Personel başkalarının türünü görmez). Onaylı izin takvimde kapalı, online randevuda o gün çıkmaz; aralıktaki randevular onay ekranında listelenir, otomatik iptal edilmez (Sprint 7A).
- **Prim hesabı (Salon Randevu):** kural kime (herkes veya personel) ve neye (bütün satışlar, hizmetler, ürünler, kategori, tek ürün/hizmet), yüzde veya adet başına sabit TL; en özel kural kazanır, toplanmaz. Taban satırın KDV hariç, indirim sonrası tutarı; prim satış satırının personeline (varsayılan satışı yapanın bağlı personel kaydı, randevudan gelen satırda randevunun personeli) satış anında yazılır. Paket primi paket satılırken; seans kullanımından ve 0 TL satırdan prim yok. Satış iptalinde iptal ayına eksi prim; kapanmış ay değişmez. Ay sonu prim raporu ("Satışı giren" sütunuyla) ve Excel (Sprint 7B).
- Dashboard'da "aktif personel" kartı (Sprint 7B).

## I. Abonelik ve Yönetim

- Paketler: Çırak (ücretsiz), Kalfa, Usta; paket limitleri (kullanıcı, müşteri, aylık randevu) yapılandırılabilir.
- 14 gün deneme, iyzico ile kartla tekrarlayan ödeme.
- **Şeffaf abonelik:** yenilemeden 7 gün önce SMS + e-posta hatırlatma; panelden tek tıkla iptal; abonelik bitince veriler silinmez, salt okunur erişim ve Excel dışa aktarma sürer.
- Destek: panelde WhatsApp destek butonu (wa.me) ve yardım makaleleri.
- Süper admin paneli (sadece KobiFabrika ekibi): işletmeler, abonelikler, kullanım, SMS kredisi, destek notları.

## Faz 1'de olmayanlar

`docs/00-urun-ozeti.md` → Fazlar bölümü.
