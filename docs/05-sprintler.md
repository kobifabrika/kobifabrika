# Sprint planı (Faz 1)

Tahmini, yarı zamanlı. Toplam yaklaşık 15-18 hafta. Her sprint sonunda: build ve testler geçer, ekranlar 375px'te kontrol edilir, bu dosyada maddeler işaretlenir, `docs/karar-gunlugu.md` güncellenir, commit atılır.

**Durum:** Sprint 0 tamamlandı (2026-10-10). Sıradaki: Sprint 1.

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

- [ ] Kayıt, e-posta doğrulama (Mailpit), giriş/çıkış, şifremi unuttum (cookie auth)
- [ ] Kurulum sihirbazı: işletme bilgileri, çalışma saatleri, sektör şablonu, slug
- [ ] Roller (Owner/Manager/Staff/Accountant), davet akışı, yetki policy'leri
- [ ] Bildirimler, aktivite günlüğü, Ctrl+K arama iskeleti
- [ ] Giriş ekranı tasarımı (taslaktaki düzen, mobil düzeltmelerle)
- [ ] Kiracı izolasyonu entegrasyon testleri

## Sprint 2 — Müşteriler ve cari (2 hafta)

- [ ] Müşteri/tedarikçi CRUD, etiketler, notlar, kaynak, KVKK izinleri
- [ ] Zaman çizelgesi, segmentler, hatırlatmalar
- [ ] Excel'den içe aktarma (şablon indir → yükle → önizle → kaydet)
- [ ] Cari hareketler ve bakiye, PDF ekstre (QuestPDF)
- [ ] Cari ekstre paylaşımı (WhatsApp/SMS/e-posta linki)
- [ ] Tahsilat linki (herkese açık sayfa, "Ödedim" → bildirim → onay → tahsilat kaydı)

## Sprint 3 — Ürün, satış, teklif (2 hafta)

- [ ] Ürün/hizmet kataloğu, kategoriler, KDV oranları
- [ ] Stok hareketleri, kritik stok uyarısı
- [ ] Hızlı satış ekranı (mobil öncelikli), ödeme tipleri, veresiye → cari
- [ ] Teklif: PDF, paylaşım linki, müşteri onayı, satışa dönüştürme
- **İlk test edilebilir sürüm (yaklaşık 7. hafta)**

## Sprint 4 — Finans ve dashboard (1-2 hafta)

- [ ] Kasa/banka hesapları, tahsilat, ödeme, transfer
- [ ] Gelir-gider kategorileri
- [ ] Veresiye ve vade listesi, vade hatırlatma (manuel + otomatik)
- [ ] Bugün ekranı (dashboard)

## Sprint 5 — Randevu (2-3 hafta)

- [ ] Personel çalışma saatleri, takvim (gün/hafta, personel kolonlu, sürükle-bırak, çakışma kontrolü)
- [ ] Online randevu sayfası `/r/[slug]` (telefon doğrulama, onay ayarı)
- [ ] Hatırlatma SMS'i (Hangfire), gelmedi işaretleme
- [ ] Randevuyu satışa çevirme
- [ ] Paket ve seans takibi
- [ ] Gelmeyen müşteri uyarısı

## Sprint 6 — Pazarlama ve İYS (2 hafta)

- [ ] SMS kredisi, MessageLog, sahte SMS sağlayıcı (gerçeği pilot öncesi)
- [ ] Kampanya akışı (segment → mesaj → önizleme/maliyet → gönder/zamanla), İYS izin süzmesi, çıkış seçeneği
- [ ] Otomasyonlar: doğum günü, geri kazanma, randevu sonrası teşekkür
- [ ] Memnuniyet anketi → Google yorum yönlendirmesi
- [ ] Boş saat kampanyası önerisi
- [ ] Risk listesi

## Sprint 7 — Personel, mesai, izin, prim (2 hafta)

- [ ] Personel kartı (maaş sadece Owner)
- [ ] Mesai giriş-çıkış, fazla mesai raporu
- [ ] İzin türleri, talep/onay, bakiye, izin takvimi; izinli personel takvimde kapalı
- [ ] Prim kuralları ve ay sonu prim raporu
- [ ] Raporlar sayfası (satış, ürün, alacak, gelir-gider, kampanya, personel) + Excel dışa aktarma

## Sprint 8 — Abonelik, destek ve pilot (1-2 hafta)

- [ ] Plan limitleri, deneme süresi, iyzico abonelik (sandbox)
- [ ] Şeffaf abonelik: 7 gün önce hatırlatma, tek tıkla iptal, süresi dolunca salt okunur mod
- [ ] WhatsApp destek butonu, yardım makaleleri iskeleti
- [ ] Süper admin paneli
- [ ] Canlı ortam: VPS, Docker Compose, Caddy, GitHub Actions, yedekleme
- [ ] Tanıtım sitesi metinleri ("Bizi farklı kılan" maddeleri), KVKK metinleri
- [ ] Pilot: 5-10 gerçek esnaf
