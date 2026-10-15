# Sprint planı (Faz 1)

Tahmini, yarı zamanlı. Toplam yaklaşık 15-18 hafta. Her sprint sonunda: build ve testler geçer, ekranlar 375px'te kontrol edilir, bu dosyada maddeler işaretlenir, `docs/karar-gunlugu.md` güncellenir, commit atılır.

**Durum:** Sprint 7A bitti (personel kartı, mesai, izin; slogan ve sosyal medya): Actions #19 yeşil (2026-10-15; commit `b70420b`; backend, Vitest 251, E2E 125, tekrar denemesiz); yerel aşamalar yeşil (backend 595, lint, Vitest, build, yeni/değişen E2E). Sıradaki: 7B (prim, raporlar, Bugün). Sprint 6 bitti: 6B (otomasyonlar, memnuniyet anketi, boş saat, kaybedilme riski) Actions #15 yeşil (2026-10-15; commit `0871fdf`; E2E 116 geçti, tekrar denemesiz): yerel aşamalar yeşil (backend 548, frontend lint + Vitest 242 + build, yeni/değişen E2E). Sıradaki: Sprint 7 (personel, mesai, izin, prim). 6A Actions #13 yeşil (commit `22ccbd3`). Sprint 5B Actions #11 (`3daba9e`), 5A Actions #7 (`3c26021`). Sprint 4 tamamlandı (2026-10-12). Hangfire Sprint 5'ten öne çekildi (Sprint 2); Raporlar'ın ilk dördü Sprint 7'den öne çekildi (Sprint 4); MessageLog ve sahte SMS Sprint 6'dan 5B'ye alındı.

---

## Sprint 0 — Kurulum (1 hafta)

- [x] Klasör yapısı (CLAUDE.md'deki gibi), `.gitignore`, `.editorconfig`, `.env.example`
- [x] `docker-compose.yml`: SQL Server 2025 + Mailpit; `docker compose up -d` ile ayağa kalkar
- [x] Backend solution: Api, Domain, Infrastructure, Tests projeleri; .NET 10
- [x] EF Core + SQL Server bağlantısı (user-secrets), `AppDbContext`, ilk migration (Businesses, Identity tabloları, BusinessMemberships)
- [x] Kiracı altyapısı: `ITenantContext`, `ITenantScoped`, global query filter, SaveChanges'te BusinessId ataması + testi
- [x] Serilog, ProblemDetails, OpenAPI + Scalar, `/health` endpoint'i
- [x] Frontend: Next.js (App Router, TS, Tailwind, ESLint, `src/`), shadcn/ui kurulumu, renk token'ları (`docs/04-tasarim.md`), lucide-react
- [x] `apiClient` + `npm run gen:api` (openapi-typescript)
- [x] Boş sayfalar: tanıtım ana sayfa (taslak), giriş, panel iskeleti (sol menü + mobil alt çubuk)
- [x] README: kurulum ve çalıştırma adımları (Windows ve Mac)
- [x] Git: ilk commit

## Sprint 1 — Çekirdek (2 hafta)

- [x] Kayıt, e-posta doğrulama (Mailpit), giriş/çıkış, şifremi unuttum (cookie auth)
- [x] Kurulum sihirbazı: işletme bilgileri, çalışma saatleri, sektör şablonu, slug
- [x] Roller (Owner/Manager/Staff/Accountant), davet akışı, yetki policy'leri
- [x] Bildirimler, aktivite günlüğü, Ctrl+K arama iskeleti
- [x] Giriş ekranı tasarımı (taslaktaki düzen, mobil düzeltmelerle)
- [x] Kiracı izolasyonu entegrasyon testleri

## Sprint 2 — Müşteriler ve cari (2 hafta)

- [x] Müşteri/tedarikçi CRUD, etiketler, notlar, kaynak, KVKK izinleri
- [x] Zaman çizelgesi, segmentler, hatırlatmalar (Hangfire ile vakti gelince bildirim)
- [x] Excel'den içe aktarma (şablon indir → yükle → sütun eşle → önizle → kaydet → rapor)
- [x] Cari hareketler ve bakiye (açılış, elle borç/alacak, Tahsilat al), PDF ekstre (QuestPDF)
- [x] Cari ekstre paylaşımı (WhatsApp/e-posta linki; SMS butonu "Yakında", Sprint 6)
- [x] Tahsilat linki (herkese açık sayfa, "Ödedim" → bildirim → onay → tahsilat kaydı)
- [x] Ayarlar'da IBAN (TR doğrulama); her işletmede Nakit Kasa ve Banka hesabı

## Sprint 3 — Ürün, satış, teklif (2 hafta)

- [x] Ürün/hizmet kataloğu, kategoriler, KDV oranları (Excel'den ürün içe aktarma dahil)
- [x] Stok hareketleri, kritik stok uyarısı
- [x] Hızlı satış ekranı (mobil öncelikli, barkod okuyucu), ödeme tipleri, bölünmüş ödeme, veresiye → cari, 80 mm fiş
- [x] Teklif: PDF, paylaşım linki, müşteri onayı, satışa dönüştürme
- **İlk test edilebilir sürüm (yaklaşık 7. hafta)**

## Sprint 4 — Finans ve dashboard (1-2 hafta)

- [x] Kasa/banka hesapları (birden çok hesap, açılış bakiyesi, varsayılan hesap, pasife alma), tahsilat, tedarikçiye ödeme, transfer, ters kayıt
- [x] Gelir-gider kategorileri ve gelir/gider kaydı (telefonda hızlı işlem "Gider ekle")
- [x] Veresiye ve vade listesi (FIFO), vade hatırlatma (WhatsApp/SMS linki + sabah uygulama içi bildirimi; müşteriye otomatik SMS Sprint 6)
- [x] Bugün ekranı (dashboard)
- [x] Raporlar sayfası: satışlar, en çok satan, alacaklar (yaşlandırma), gelir-gider + Excel (Sprint 7'den öne çekildi)

## Sprint 5 — Randevu (2-3 hafta)

İki parça: **5A** takvim ve randevu, **5B** online randevu, SMS altyapısı, hatırlatma ve paketler.

- [x] Personel (temel kart: ad, unvan, telefon, takvim rengi, online randevuda görünür, bağlı hesap), haftalık çalışma saatleri (öğle arası), verdiği hizmetler, mola/izin, tatil günleri (5A)
- [x] Takvim (gün/hafta, masaüstünde personel kolonlu, sürükle-bırak ve süre uzatma, telefonda personel seçici ve "Taşı"), sunucuda eşzamanlılığa dayanıklı çakışma kontrolü (5A)
- [x] Online randevu sayfası `/r/[slug]` (telefon doğrulama, onay ayarı, müşteri iptal linki `/r/[slug]/randevu/[token]`) (5B)
- [x] Hatırlatma SMS'i (Hangfire) (5B); [x] gelmedi işaretleme (5A)
- [x] Randevuyu satışa çevirme (5A)
- [x] Paket ve seans takibi (5B)
- [x] Gelmeyen müşteri uyarısı (5A)
- [x] Bugün ekranında "Bugünün randevuları" (Sprint 7'den öne çekildi) (5A)
- [x] SMS altyapısı: `ISmsSender`, MessageLog, sahte sağlayıcı (Mailpit yansıması), panelden onay/ret/taşıma/iptal SMS'leri (Sprint 6'dan öne çekildi) (5B)

## Sprint 6 — Pazarlama ve İYS (2 hafta)

İki parça: **6A** kredi, kampanya, İYS ve KVKK; **6B** otomasyonlar, anket, boş saat, risk listesi.

- [x] SMS kredisi ve düşümü: her ücretli SMS segment kadar atomik düşer, eksiye düşmez, hata iade; bilgilendirme SMS'leri de düşer, doğrulama kodu düşmez; düşük bakiye ve "kredi bitti" bildirimi; hoş geldin kredisi (50); platform yüklemesi (PlatformAdmin ekranı) (6A)
- [x] Kampanya akışı (segment → mesaj → önizleme/maliyet → gönder/zamanla), 09:00-21:00, günde en fazla 1 pazarlama SMS'i, iptal, Hangfire dağıtıcısı (6A)
- [x] İYS izin süzmesi, çıkış seçeneği (kısa link /c/[kod] + RET), IIysClient + sahte istemci (6A)
- [x] KVKK: müşteri verisini dışa aktarma (Excel/JSON) ve kişisel verileri silme (anonimleştirme) (Faz 1 A'dan) (6A)
- [x] Devredenler: sürükle-bırakta "Müşteriye SMS ile bildirilsin mi? [Gönder]", mobil alt çubuk (5 düğme, Menü sağ üstte), ortak çip/sekme satırı (6A)
- [x] Otomasyonlar: doğum günü, geri kazanma, randevu sonrası teşekkür, vade günü hatırlatması (6B)
- [x] Memnuniyet anketi → Google yorum yönlendirmesi (6B)
- [x] Boş saat kampanyası önerisi (6B)
- [x] Risk listesi (6B)

## Sprint 7 — Personel, mesai, izin, prim (2 hafta)

İki parça: **7A** personel kartı, mesai, izin; **7B** prim, raporlar, Bugün.

- [x] Personel kartı (iş başı, doğum tarihi, not; maaş Sahip ve Mali Müşavir; ödenen maaşlar) (7A)
- [x] Mesai giriş-çıkış (tek düğme, yönetici düzeltmesi, unutulan çıkış), fazla mesai raporu (7A)
- [x] İzin türleri, talep/onay, bakiye (İş Kanunu 53, yıl dönümü), izin takvimi; izinli personel takvimde ve online randevuda kapalı (7A)
- [x] Slogan "KOBİ'nin dijital fabrikası" ve sosyal medya hesapları sitede (7A ile)
- [ ] Prim kuralları ve ay sonu prim raporu
- [ ] Kampanya ve personel raporları + Excel dışa aktarma (satış, ürün, alacak, gelir-gider raporları Sprint 4'te geldi)
- [ ] Bugün ekranına aktif personel kartı (randevu kartı Sprint 5A'da geldi)

## Sprint 8 — Abonelik, destek ve pilot (1-2 hafta)

- [ ] Plan limitleri, deneme süresi, iyzico abonelik (sandbox)
- [ ] Şeffaf abonelik: 7 gün önce hatırlatma, tek tıkla iptal, süresi dolunca salt okunur mod
- [ ] WhatsApp destek butonu, yardım makaleleri iskeleti
- [ ] Süper admin paneli
- [ ] Canlı ortam: VPS, Docker Compose, Caddy, GitHub Actions, yedekleme; forwarded headers (IP hız sınırları Caddy arkasında gerçek istemci IP'siyle çalışsın; Sprint 5B notu)
- [ ] Tanıtım sitesi metinleri ("Bizi farklı kılan" maddeleri), KVKK metinleri
- [ ] Pilot: 5-10 gerçek esnaf
