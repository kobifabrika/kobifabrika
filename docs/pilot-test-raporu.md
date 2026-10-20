# Pilot öncesi test raporu (Sprint 8A, A10)

Tarih: 2026-10-21 · Durum: tur tamamlandı, bulgular aşağıda.

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
