# Sprint planı (Faz 1)

Tahmini, yarı zamanlı. Toplam yaklaşık 15-18 hafta. Her sprint sonunda: build ve testler geçer, ekranlar 375px'te kontrol edilir, bu dosyada maddeler işaretlenir, `docs/karar-gunlugu.md` güncellenir, commit atılır.

**Durum:** Sprint 8 (pilot yayını) sürüyor. 8A yayın öncesi kesiti bitti: Actions #31 yeşil (2026-10-21; commit `412e9a7`; backend 656, Vitest 271, E2E 149 geçti, tekrar denemesiz); A10 turu ve ekran ekran bulgular `docs/pilot-test-raporu.md`. 8B canlı ortam kuruldu (2026-10-23; KVM sunucu, site yayında, 2026-10-24 tam mod (kayıt/giriş/panel) açıldı, API + veritabanı çalışıyor, `docs/canli-ortam.md`); otomatik dağıtım çalışıyor (#60), Cloudflare turuncu bulut ve 80/443 sadece Cloudflare'e (2026-10-24); e-posta (Brevo), Sentry, R2 hazır (2026-10-24); tam mod açık (2026-10-24); kalan: kontrol listesi turu (`docs/canli-ortam.md`), UptimeRobot, pilot; 8A "yayından sonra" maddeleri pilotla birlikte. Sprint 7 bitti (Actions #22 ve #23 yeşil): 7B (prim kuralları, satır personeli, Primim, prim/personel/kampanya/anket raporları, Bugün'de aktif personel) Actions #22 yeşil (2026-10-15; commit `8e0a5f8`; backend, Vitest 262, E2E 133 geçti; 1 flaky: `sprint1-hesap` telefon-chromium ilk denemede çıkış sonrası prefetch 404'ü, uygulamada düzeltildi: `proxy.ts`). Sıradaki: Sprint 8 (abonelik, destek, canlı ortam, pilot). Sprint 7A bitti (personel kartı, mesai, izin; slogan ve sosyal medya): Actions #19 yeşil (2026-10-15; commit `b70420b`; backend, Vitest 251, E2E 125, tekrar denemesiz); yerel aşamalar yeşil (backend 595, lint, Vitest, build, yeni/değişen E2E). Sıradaki: 7B (prim, raporlar, Bugün). Sprint 6 bitti: 6B (otomasyonlar, memnuniyet anketi, boş saat, kaybedilme riski) Actions #15 yeşil (2026-10-15; commit `0871fdf`; E2E 116 geçti, tekrar denemesiz): yerel aşamalar yeşil (backend 548, frontend lint + Vitest 242 + build, yeni/değişen E2E). Sıradaki: Sprint 7 (personel, mesai, izin, prim). 6A Actions #13 yeşil (commit `22ccbd3`). Sprint 5B Actions #11 (`3daba9e`), 5A Actions #7 (`3c26021`). Sprint 4 tamamlandı (2026-10-12). Hangfire Sprint 5'ten öne çekildi (Sprint 2); Raporlar'ın ilk dördü Sprint 7'den öne çekildi (Sprint 4); MessageLog ve sahte SMS Sprint 6'dan 5B'ye alındı.

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
- [x] Prim kuralları (kime, neye, yüzde/adet başına; en özel kazanır), satır personeli, ay sonu prim raporu, iptalde eksi prim, Primim (7B)
- [x] Kampanya, anket ve personel raporları + Excel dışa aktarma (satış, ürün, alacak, gelir-gider raporları Sprint 4'te geldi) (7B)
- [x] Bugün ekranına aktif personel kartı ve Personel rolünde "Mesaim" kartı (randevu kartı Sprint 5A'da geldi) (7B)

## Sprint 8 — Pilot yayını (1-2 hafta)

Şirket henüz kurulmadı: ödeme alma (iyzico), gerçek SMS sağlayıcısı ve İYS bağlantısı ertelendi (aşağıda "Faz 1 sonu / şirket kurulunca"); altyapıları kodda kalır, kapalıdır. 5-10 gerçek işletmeyle (pilates stüdyosu, kafe-restoran, küçük yemek restoranı, iki kuaför) ücretsiz pilot. İki parça: **8A** kod (şimdi), **8B** canlı ortam (sunucu alınınca). Plan ve kararlar: karar günlüğü 2026-10-17.

### 8A — Yayın öncesi

- [x] Paketler ve abonelik (ödemesiz; pilot kısmı: Plans/Subscription, deneme, limitler, Pilot, salt okunur, `?paket=` kaydı; abonelik ekranı cilası yayından sonra): Plans tablosu (Çırak 0 TL · 1 kullanıcı, 100 müşteri, ayda 50 randevu; Kalfa 699 TL/ay, 6.990 TL/yıl · 3, 1.000, 500 · kampanya, otomasyon, online randevu; Usta 1.299 TL/ay, 12.990 TL/yıl · 10, sınırsız · hepsi; KDV hariç, Mali Müşavir sayılmaz), 14 gün deneme (bütün özellikler, Usta limitleri), deneme bitince Çırak (veri silinmez, kapalı özellikler salt okunur), limit aşımında anlaşılır mesaj, Pilot (süresiz, limitsiz, etiketli), salt okunur mod, `?paket=` kaydı
- [x] Süper admin paneli `/platform` (çekirdek): işletmeler (arama, filtre, sayılar, son giriş), ayrıntı (paket, pilot, deneme uzatma/bitirme, salt okunur, kullanıcılar, kredi, destek notları, platform günlüğü), geri bildirimler; müşteri verisi görünmez
- [x] SMS'siz çalışma: `Messaging:SmsEnabled=false`; randevu bildirimleri e-postayla, WhatsApp düğmeleri, "Yarının randevuları" + "Hatırlatıldı", anket linki WhatsApp'tan, vade hatırlatma e-postası; online randevu kodsuz (işletme onaylar, e-posta isteğe bağlı, bot tuzağı ve hız sınırları)
- [x] E-posta altyapısı (kod; Brevo hesabı ve DNS 8B'de): Brevo (SMTP), `bildirim@kobifabrika.com`, Reply-To işletme, Hangfire kuyruğu (OutboundEmails), logolu sade şablonlar
- [x] Destek ve yardım: Yardım menüsü (WhatsApp +90 535 977 93 82, destek@kobifabrika.com, makaleler), geri bildirim (metin + isteğe bağlı ekran görüntüsü), 4 temel yardım makalesi
- [x] Hukuki metin taslakları (son güncelleme tarihli): KVKK aydınlatma, gizlilik, çerez, kullanım koşulları, veri işleme sözleşmesi, işletmenin müşterileri için aydınlatma şablonu; şirket bilgisi tek ayardan (şimdilik sadece "KobiFabrika"; 2026-10-21)
- [x] Tanıtım sitesi yeniden tasarımı (5 marka rengi), "Işıltı Kuaför" demo ekran görüntüleri, fiyat/SSS/hakkımızda/iletişim/yardım/hukuki sayfalar, SEO (başlık, açıklama, OG görseli, sitemap, robots)
- [x] Panel ve giriş ekranları renk yenilemesi (#000a1d koyu yüzeyler, #006ea6 link/seçili, #00b0ff ana düğme; Bugün kartlarında renkli ikon ve şerit, boş durum çizimleri)
- [x] Baştan sona tur: her rol ve ekran masaüstü + 375 px, `docs/pilot-test-raporu.md`; online randevu sayfası ve müşteri e-postalarına ayrı özen
- [x] E2E "yeni esnaf" senaryosu (`sprint8a-yeni-esnaf`): kayıt → doğrulama → kurulum → personel → hizmet → online randevu → onay → tamamlandı → satış → kasa → Bugün
- [x] Logo ve ikonlar: logo sitede, giriş/kayıtta, panel menüsünde ve sistem e-postalarında; favicon ve uygulama ikonları "KF" işaretinden (`public/marka/`, `npm run marka`)
- Yayın öncesi kontrol listesi (A8 + A10):
  - [x] Özel 404 ve genel hata sayfası (site, panel, /r ve kök), Türkçe ve yönlendirici
  - [x] Bütün görsellerde anlamlı alt metin (sprint8a-site kontrol eder)
  - [x] İlk ekranda "Ücretsiz dene"; telefonda altta sabit "Ücretsiz dene"
  - [x] Kayıt sonrası "E-postanı kontrol et" ekranı
  - [x] Görseller next/image ile, WebP, ekran genişliğine göre boyut
  - [x] Cloudflare Web Analytics (çerezsiz; `NEXT_PUBLIC_CF_ANALYTICS_TOKEN` boşsa eklenmez; gizlilik ve çerez metninde)
  - [x] PageSpeed/Lighthouse mobil puanları (`docs/pilot-test-raporu.md`)

### 8A — Yayından sonra

- [ ] Abonelik ekranı cilası, deneme bitişi bildirimleri (`trial-notices`: 3 gün önce ve bitince uygulama içi + e-posta)
- [ ] Kampanya ve otomasyonlarda e-posta ve WhatsApp kanalı (WhatsApp'ta "Sıradaki" ile tek tek)
- [ ] Yardım merkezinin kalan makaleleri (personel ve randevu, satış ve veresiye, kasa ve gider, kampanya, raporlar, ekip, mali müşavir, dışa aktarma, paketler)
- [ ] Süper admin: sistem ekranı (son hatalar, Hangfire, e-posta kuyruğu), Plans düzenleme ekranı

### 8B — Canlı ortam (sunucu alınınca)

- [x] Alan adları: kobifabrika.com ve kobifabrika.com.tr alındı, nameserver'lar Cloudflare'e taşındı (2026-10-17)
- [x] Ara adım: tanıtım sitesi geçici sunucuda yayında (2026-10-21; Güzel Hosting OpenVZ, sadece-site modu, Caddy + Let's Encrypt; `docs/canli-ortam.md` → Geçici site sunucusu)
- [x] Türkiye lokasyonlu sunucu (2026-10-23: Güzel Hosting Private Cloud 2, KVM, 4 GB, Ubuntu 24.04, 70.40.139.67): deploy kullanıcısı, sadece SSH anahtarı (root/şifre kapalı), ufw (22/80/443), fail2ban, otomatik güvenlik güncellemeleri, 4 GB swap
- [x] Docker Compose (`deploy/canli/`): API, Next.js, SQL Server 2025 Express, Caddy (otomatik HTTPS); app./api. alt alan adları, çerez `.kobifabrika.com`, bellek sınırları, Hangfire paneli PlatformAdmin, Data Protection sertifikayla şifreli, gizli değerler sunucu `.env` + `deploy/gizli-ekle.ps1`, yayın modu `site|tam`
- [x] kobifabrika.com.tr → kobifabrika.com (Cloudflare yönlendirme kuralı, 301, yol ve sorgu korunur; 2026-10-23)
- [x] Forwarded headers (Caddy `trusted_proxies` + `CF-Connecting-IP`, API `ForwardedHeaders:KnownNetworks`); turuncu bulut (2026-10-24: Full (strict), gerçek IP Caddy ve API kaydında doğrulandı, 80/443 sadece Cloudflare aralıklarına `bin/cloudflare-ip.sh`, Let's Encrypt HTTP-01 Cloudflare arkasında denendi)
- [x] E-posta: Brevo (ücretsiz, günde 300; uygulama sınırı 280), DKIM + DMARC, Cloudflare Email Routing (destek@, bildirim@ → kobifabrika@gmail.com); Gmail'de SPF/DKIM/DMARC PASS, Gelen Kutusu (2026-10-24)
- [x] GitHub Actions: testler yeşilse otomatik dağıtım, yedek, migration bundle, `/health`, önceki imaja geri alma (ilk otomatik dağıtım #60, 2026-10-23, `67a28dd`)
- [x] Yedekleme: gece yedeği (03:30 İstanbul) + dağıtım öncesi, 7 gün yerel (zstd; Express sıkıştırma desteklemiyor), geri yükleme denemesi
- [x] Cloudflare R2'ye şifreli kopya (2026-10-24, `kobifabrika-yedek/veritabani/`)
- [x] Sentry: API + web (sunucu tarafı), kişisel veri maskeli, performans izleme kapalı; canlı deneme olayları gönderildi (2026-10-24)
- [ ] İzleme: UptimeRobot
- [x] `docs/canli-ortam.md` (kurulum, dağıtım, geri dönüş, yedekten dönme, gizli değerler, yayın modu, taşıma, Cloudflare, sorun giderme, benden istenecekler)
- [ ] Pilot: 5-10 gerçek esnaf

## Faz 1 sonu / şirket kurulunca

- [ ] iyzico abonelik (sandbox → canlı; `IPaymentProvider` ve sahte uygulama 8A'da kapalı duruyor), Payments tablosu, "Paket seç" ile online ödeme
- [ ] Şeffaf abonelik: yenilemeden 7 gün önce SMS + e-posta hatırlatma, tek tıkla iptal
- [ ] Mesafeli satış sözleşmesi, şirket unvanı/adres/vergi bilgisinin ayara girilmesi, e-Arşiv fatura
- [ ] Gerçek SMS sağlayıcısı (Netgsm / İleti Merkezi), `Messaging:SmsEnabled=true`, SMS kredisi satışı
- [ ] Gerçek İYS bağlantısı (`IIysClient`)
