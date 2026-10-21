# Pilot öncesi test raporu (Sprint 8A, A10)

Tarih: 2026-10-21 · Durum: tur tamamlandı, bulgular düzeltildi; Actions #31 yeşil (`412e9a7`).

## Yöntem

- **Demo verisi:** "Işıltı Kuaför" (`frontend/e2e/demo/seed.ts`): 3 personel, 12 hizmet, 60 müşteri, bugün 10 ve yarın 5 randevu, nakit/kart/veresiye satışlar, vadeli borçlar, gider, açık online randevu sayfası. E2E ortamında (`KobiFabrika_E2E`, SMS kapalı); geliştirme veritabanına dokunmaz. Bütün veriler uydurmadır.
- **Tur betiği:** `npx playwright test -c playwright.demo.config.ts tur` (`frontend/e2e/demo/tur.demo.ts`). Her rol (Sahip, Yönetici, Personel — takvimde kendi kartına bağlı —, Mali Müşavir, Süper admin) ve oturumsuz müşteri; her ekran masaüstünde (1280×800) ve telefonda (375×812, 2x) tam sayfa görüntü. Otomatik bulgular: 375 px'te yatay taşma, axe ciddi/kritik ihlal (renk kontrastı dahil), 5xx yanıt, konsol hatası (`test-results/tur/bulgular.json`).
- **İnsan gözü:** görüntüler tek tek açılıp kesik metin, taşma, sıkışık düğme, boşluk, Türkçe, boş durum, hata ve yükleniyor halleri için incelendi.
- **Müşteriye görünenler ayrıca:** online randevu sayfası (kodsuz akış, aydınlatma, bulunamadı), müşteriye giden e-postalar (Mailpit önizlemesi, telefon genişliği).
- **Uçtan uca:** `sprint8a-yeni-esnaf` (kayıt → doğrulama → kurulum → personel → hizmet → online randevu → onay → tamamlandı → satış → kasa → Bugün), `sprint8a-online-kodsuz`, `sprint8a-platform`, `sprint8a-site` ve bütün eski senaryolar (SMS kapalı çift + `@sms` çifti).

## Lighthouse (mobil)

Lighthouse 12.8.2, varsayılan mobil emülasyon (Moto G Power, yavaş 4G, 4x CPU yavaşlatma), production derlemesi (`next start`), yerel makine. Canlı sunucuda (Türkiye VDS + Cloudflare) LCP daha iyi olur; 8B'de PageSpeed Insights ile yeniden ölçülecek.

| Sayfa | Performans | Erişilebilirlik | En iyi uygulamalar | SEO | LCP | CLS | TBT |
|---|---:|---:|---:|---:|---:|---:|---:|
| `/` | 91 | 100 | 100 | 100 | 3,5 sn | 0 | 60 ms |
| `/fiyatlar` | 93 | 100 | 100 | 100 | 3,3 sn | 0 | 40 ms |
| `/kayit` | 88 | 100 | 96 | 100 | 3,8 sn | 0 | 70 ms |
| `/yardim/ilk-kurulum` | 91 | 100 | 100 | 100 | 3,4 sn | 0 | 70 ms |

Ölçümle yapılan düzeltmeler:
- Ana sayfa 86 → 91: giriş bölümündeki belirme animasyonu kaldırıldı (en büyük öğe, "Ücretsiz dene" düğmesi, animasyon bitene kadar boyanmamış sayılıyordu); telefonda gizli olan laptop görseli artık önyüklenmiyor.
- Kayıt 82 → 88: bütün form adres parametresini okuyan Suspense sınırının içindeydi ve sadece istemcide çiziliyordu; sınır yalnızca "Seçtiğiniz paket" bandını sarıyor, form sunucuda çiziliyor (TBT 230 → 70 ms).
- Fiyatlar erişilebilirlik 98 → 100: başlık sırası (h1'den h3'e atlıyordu).
- Kayıtta "En iyi uygulamalar" 96: oturum yoklamasının (`/api/auth/me`) oturumsuz ziyaretçideki 401 yanıtı tarayıcı konsoluna düşüyor; beklenen davranış, açık bırakıldı.

## Ekran ekran bulgular (tur 2026-10-21)

Tur betiği bütün rollerde 139 ekranı masaüstünde ve 375 px'te açtı (278 görüntü). Otomatik kontroller (375 px taşma, axe ciddi/kritik, 5xx, konsol hatası) **0 bulgu** verdi. Görüntüler ayrıca gözle incelendi. Müşteri e-postalarının hepsi (randevu: alındı, onaylandı, hatırlatma, saat değişti, iptal, onaylanamadı; ekstre, tahsilat, vade hatırlatma, teklif; sistem: doğrulama, şifre, davet, deneme) gerçek şablon koduyla 375 px'te görüntülendi.

Durum: **bakıldı** (sorun yok) · **düzeltildi** · **açık** (pilot sonrasına).

| Rol / yer | Ekran | Durum | Not |
|---|---|---|---|
| Sahip | Bütün panel ekranları | bakıldı | Önceki turda düzeltilenler: docs/04 ve karar günlüğü 2026-10-21. |
| Sahip, Yönetici | Bugün (masaüstü) | düzeltildi | "Son 30 gün satış" sütunları giriş animasyonu bitmeden boş görünebiliyordu; animasyon kapatıldı (tasarım kuralı: animasyon yok). |
| Yönetici | Bugün, takvim, satış, müşteri, cari/kasa, personel kartı (maaş yok), ayarlar, ekip | bakıldı | Sahip ile aynı; maaş sekmesi görünmüyor. |
| Personel | Bugün (kendi randevuları, Mesaim, kendi satışları), takvim, Mesai ve İzin, Primim | bakıldı | Finans kartları ve raporlar yok; yetkisiz adreslerde "Bu sayfayı görme yetkin yok". |
| Personel, Yönetici, Sahip | Takvim gün/hafta | düzeltildi | En üstteki saat etiketi (09:00) yapışkan personel başlığının altında yarısı kesiliyordu. |
| Mali Müşavir | Bugün | düzeltildi | "Yarının randevuları"nda her satırda "Cep telefonu yok", WhatsApp açıklaması ve "Hatırlatılan 0/5" görünüyordu; salt okunur rolde liste kalır, hatırlatma kısmı yok. |
| Mali Müşavir | Alt menü (telefon) | düzeltildi | "+" düğmesi boş "Hızlı işlem" penceresi açıyordu; işlem yoksa düğme yok (Vitest + E2E). |
| Mali Müşavir | Kampanyalar, Otomasyonlar, raporlar, cari/kasa, satışlar | bakıldı | Salt okunur, düğmeler yok. |
| Mali Müşavir | Personel kartı | açık | Devre dışı form olarak görünüyor; okunur bir özet görünümü daha iyi olur. |
| Herkes (SMS kapalı) | Otomasyonlar | düzeltildi | "Son 30 gün: 0 SMS · 0 kredi" satırı SMS kapalıyken gizli; anahtarlar kilitli ve açıklamalı (E2E). |
| Süper admin | Özet, İşletmeler, işletme ayrıntısı, kullanıcılar, kredi, notlar, geri bildirim | düzeltildi | Telefonlar ham `+902125554433` yerine `0212 555 44 33`. "Son giriş" kayıttan sonra hiç çıkış yapmayan kullanıcıda hep "yok" kalıyordu; kayıt ve davetle kayıt ilk giriş sayılıyor. |
| Müşteri | Online randevu (telefon) | düzeltildi | Adım geçişleri sunucuya gidip başlığı (title) bir an boşaltıyordu (Actions'ta axe buldu); adımlar yerel history ile, daha hızlı. |
| Müşteri | Online randevu (masaüstü) | düzeltildi | İlk adımda sağ kolon boştu; "Nasıl çalışır?" kutusu, hizmet seçilince özet. |
| Müşteri | Aydınlatma, bulunamadı, site, kayıt, giriş, 404 | bakıldı | |
| Müşteri e-postaları | Randevu iptal / ret / saat değişti | düzeltildi | Dipnotta "işletmeyle iletişime geçin. Değişiklik için işletmeyi arayın: …" tekrarı; telefon tek cümlede. |
| Müşteri e-postaları | Randevu hatırlatma | düzeltildi | "Işıltı Kuaför randevunuzu hatırlatırız" → "Işıltı Kuaför olarak Saç kesimi randevunuzu hatırlatırız." |
| Müşteri e-postaları | Ekstre, tahsilat, teklif | açık | Altta işletmenin telefonu/adresi yok (sadece adı); vade hatırlatma ve randevu e-postalarında var. |
| Panel geneli | Hitap | açık | Panel çoğunlukla "siz" ("İşletmenizde bugün olanlar"), bazı sistem metinleri "sen" ("Bu sayfayı görme yetkin yok"); tek hitaba karar verilmeli. |
| Test | `import-wizard.test.tsx` (Vitest) | açık | Yerelde tam Vitest koşusunda yük altında 5 sn sınırını aşabiliyor (tek başına 1,5 sn; Actions #31'de geçti). |

Actions'ta bulunan (2026-10-20 gecesi): `sprint8a-yeni-esnaf` İstanbul saatiyle 00:00-01:00 arası koşunca randevuyu gece yarısını aşan bir saate taşımaya çalışıyordu (API haklı olarak reddetti); test saat seçimi düzeltildi, kural gevşetilmedi.
