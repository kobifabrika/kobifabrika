# KobiFabrika — Claude Code proje talimatları

Bu dosya Claude Code'un her oturumda okuduğu proje hafızasıdır. Kısa tut; ayrıntılar `docs/` klasöründe.

## Ürün

KobiFabrika, Türkiye'deki küçük ve orta ölçekli işletmeler ve esnaf için web tabanlı (SaaS) işletme yazılımıdır.
Tek uygulamada hem parayı takip eder (cari, kasa, veresiye, stok, satış, teklif) hem müşteri getirir
(randevu, SMS kampanyası, Google yorum, geri kazanma) hem de personeli yönetir (mesai, izin, prim).

- Şirket: kuruluş aşamasında; unvan, adres ve vergi bilgisi tek ayardan (`frontend/src/content/company.ts`, backend `Company:*`), şimdilik sadece "KobiFabrika" ("kuruluş aşamasında" ifadesi hiçbir yerde görünmez) · Ürün adı her yerde **KobiFabrika** (eski çalışma adı KOBIOS; kodda, arayüzde, metinlerde KOBIOS geçmeyecek).
- Mobil uygulama yok. Arayüz responsive ve **mobil öncelikli**; esnaf çoğunlukla telefondan kullanır.
- Arayüz dili Türkçe. Para birimi TL. Saat dilimi Europe/Istanbul.

Ayrıntılar: `docs/00-urun-ozeti.md`, `docs/01-faz1-kapsam.md`.

## Teknoloji

| Katman | Seçim |
|---|---|
| Backend | C# · ASP.NET Core (.NET 10 LTS) · Minimal API · modüler monolit |
| Veritabanı | SQL Server 2025 (geliştirmede Docker'da Developer/Express) · EF Core 10 · gerektiğinde T-SQL |
| Kimlik | ASP.NET Core Identity, httpOnly cookie |
| Arka plan işleri | Hangfire (SQL Server depolama) |
| PDF | QuestPDF |
| Frontend | Next.js (App Router) · TypeScript · Tailwind CSS · shadcn/ui |
| API sözleşmesi | OpenAPI (Microsoft.AspNetCore.OpenApi) → `openapi-typescript` ile frontend tipleri |
| Test | xUnit v3 + Testcontainers + Shouldly (backend), Vitest + Testing Library (frontend), Playwright + axe-core (uçtan uca, `frontend/e2e`), GitHub Actions |

Mimari ayrıntılar: `docs/02-mimari.md`. Veri modeli: `docs/03-veri-modeli.md`.

## Klasör yapısı

```
kobifabrika/
├─ CLAUDE.md
├─ README.md                 # kurulum ve çalıştırma (Windows + Mac)
├─ global.json               # .NET SDK sürümü + test runner (Microsoft Testing Platform)
├─ docker-compose.yml        # SQL Server + Mailpit (geliştirme)
├─ .env.example              # gizli değerlerin şablonu (.env git'e girmez)
├─ docs/                     # ürün, kapsam, mimari, veri modeli, tasarım, sprintler, karar günlüğü
├─ backend/
│  ├─ KobiFabrika.slnx
│  ├─ Directory.Packages.props        # tüm NuGet sürümleri tek yerde
│  ├─ src/KobiFabrika.Api/            # host, endpoint'ler (Features/<Modul>/), Common/ (middleware), Program.cs
│  ├─ src/KobiFabrika.Domain/         # entity'ler, enum'lar, domain kuralları (Common/: TenantEntity, arayüzler)
│  ├─ src/KobiFabrika.Infrastructure/ # AppDbContext, Persistence/Migrations, Tenancy/, Identity/; ileride SMS, e-posta, Hangfire, PDF
│  └─ tests/KobiFabrika.Tests/        # xUnit v3, Testcontainers ile gerçek SQL Server
├─ test.ps1                  # tek komutla bütün testler
└─ frontend/                 # Next.js uygulaması
   ├─ e2e/                   # Playwright uçtan uca testleri (support/: fixture'lar, scripts/: E2E sunucuları)
   └─ src/app/
      ├─ (site)/             # tanıtım sitesi: kobifabrika.com
      ├─ (auth)/             # giriş, kayıt, şifre sıfırlama
      ├─ (panel)/panel/      # işletme paneli, URL /panel/... (canlıda app.kobifabrika.com buraya yönlenir)
      └─ r/[slug]/           # işletmenin herkese açık online randevu sayfası
```

## Komutlar

```powershell
.\baslat.ps1                                          # Windows: Docker + migration + API ve frontend pencereleri + tarayıcı (-Hizli: frontend production modunda)
.\durdur.ps1                                          # API, frontend ve konteynerleri durdurur (-DockerKalsin: konteynerler açık kalır)
docker compose up -d                                  # SQL Server + Mailpit (önce .env.example → .env)
dotnet run --project backend/src/KobiFabrika.Api      # API: http://localhost:5080 (/scalar, /health)
.\test.ps1                                            # BÜTÜN testler + Türkçe özet (-Hizli, -E2E, -Gorunur, -Rapor); README → Testler
dotnet test backend                                   # Docker açık olmalı (Testcontainers)
dotnet ef migrations add <Ad> -p backend/src/KobiFabrika.Infrastructure -s backend/src/KobiFabrika.Api -o Persistence/Migrations
dotnet ef database update -p backend/src/KobiFabrika.Infrastructure -s backend/src/KobiFabrika.Api
cd frontend; npm run dev                              # http://localhost:3000 (panel: /panel)
cd frontend; npm run gen:api                          # API çalışırken OpenAPI'den TS tiplerini üret, commit'le
cd frontend; npm run lint; npm run test; npm run build
```

Bağlantı dizesi user-secrets'ta (`ConnectionStrings:Default`); ilk kurulum adımları `README.md`'de.
Windows'ta API çalışırken backend derlenmez (DLL kilitli); önce API'yi durdur.
Claude oturumunda dev sunucusu (`npm run dev`, `dotnet run`) arka planda açıldıysa iş bitince kapat: oturum kapanınca çıktı borusu
kopar, sahipsiz Next dev sunucusu `write EPIPE` ile bozulur (sayfalar dakikalarca "Rendering", konsolda "Failed to fetch").

## Kurallar

- **Kod dili İngilizce, arayüz Türkçe.** Sınıf, tablo, değişken adları İngilizce (`Customer`, `Appointment`); kullanıcının gördüğü her metin Türkçe. Terim eşlemesi: `docs/03-veri-modeli.md` başındaki sözlük.
- **Çok kiracılı (multi-tenant):** İşletmeye ait her tabloda `BusinessId` var. EF Core global query filter ile otomatik süzülür; bir işletme başka işletmenin verisini asla göremez. Yeni işletme tablosu `TenantEntity`'den türer (BusinessId, soft delete, audit otomatik). BusinessId olup kiracı filtresine girmeyecek tablo `[TenantExempt("gerekçe")]` ister; yoksa `TenantModelTests` kırılır. Her modülde "A, B'nin kaydını göremez/değiştiremez" testi yaz.
- **Para:** `decimal(18,2)`, asla `float/double`. KDV oranları tabloda tutulur.
- **Zaman:** Veritabanında UTC (`datetime2`), arayüzde Europe/Istanbul.
- **Telefon:** `+90` ile E.164 biçiminde saklanır.
- **Silme:** Ana kayıtlarda soft delete (`IsDeleted`); her kayıtta `CreatedAt`, `CreatedBy`, `UpdatedAt`.
- **KVKK ve İYS:** SMS/e-posta izni olmayan müşteriye pazarlama mesajı gönderilmez. Her pazarlama mesajında çıkış seçeneği olur.
- **Yetki:** 4 rol: Owner (Sahip), Manager (Yönetici), Staff (Personel), Accountant (Mali Müşavir, salt okunur). Maaş bilgisini sadece Owner görür.
- **Gizli bilgiler** `.env` / user-secrets'ta; koda ve git'e asla girmez.
- **Tasarım:** Renkler ve kurallar `docs/04-tasarim.md`: siyah, beyaz, vurgu mavisi #00b0ff (ana düğme, yazı siyah), koyu lacivert #000a1d (koyu yüzeyler), link mavisi #006ea6; #00385a ve #00fdff sadece koyu zeminde (+ renksiz griler; kırmızı sadece hata/silme). #00b0ff ve #00fdff beyaz zeminde yazı rengi olarak kullanılmaz; gradyan, gölgeli kart, emoji yok.
- **URL'ler:** Frontend yolları Türkçe (`/panel/musteriler`, `/giris`), API yolları İngilizce (`/api/customers`).
- **Mobil:** En küçük yazı 12px, input yazısı 16px, dokunma alanı en az 44px. Her ekranı 375px genişlikte kontrol et.
- Küçük, okunur commit'ler; mesajlar Türkçe ve emir kipinde ("Müşteri listesini ekle").
- **Bitti tanımı:** GitHub Actions tam koşusu yeşil (bütün E2E senaryoları orada üç profilde koşar). Yerelde geliştirme
  sırasında yeni ve değişen senaryolar tek profille (telefon-chromium) koşulur; yerelde tam koşu gerekmez. Yeni her ekranın
  E2E senaryosu olur (`frontend/e2e/`, masaüstü + 375 px). E2E gerçek bir hata bulursa test gevşetilmez, uygulama düzeltilir.

## Çalışma şekli

1. Her sprint başında `docs/05-sprintler.md`'deki ilgili sprinti oku, önce plan çıkar (plan modu), onaydan sonra kodla.
2. Bir karar alındığında `docs/karar-gunlugu.md`'ye tarihli satır ekle.
3. Sprint bitince `docs/05-sprintler.md`'de maddeleri işaretle ve "Durum" satırını güncelle.
4. Belirsiz bir ürün kararı varsa tahmin etme; sor.
5. Geliştirme ana makinesi Windows (PowerShell). Komutlar Mac'te de çalışmalı; satır sonları LF (`.gitattributes`).
6. Her sprint sonunda ve her büyük adımdan sonra commit at ve git push ile GitHub'a gönder (origin: github.com/kobifabrika/kobifabrika). Silme veya geri alınamaz bir işlem yapmadan önce commit at.

## Güncel durum

- Sprint: **8 (pilot yayını)** sürüyor: 8A yayın öncesi kesiti bitti (Actions #31 yeşil, `412e9a7`; paket/pilot, SMS'siz çalışma, e-posta kuyruğu, süper admin `/platform`, hukuki taslaklar, site ve panel yenilemesi, A10 tur ve `docs/pilot-test-raporu.md`); sıradaki 8B canlı ortam (sunucu alınınca). 2026-10-21 tasarım turu düzeltmeleri yapıldı; A/B/C renk yönleri reddedildi, tanıtım sitesi yeni yönle (ikas + Stripe + Paraşüt) yenileniyor, panel sonra. Online randevu adımları yerel history ile (`router.push` değil). Kararlar `docs/karar-gunlugu.md` 2026-10-17 … 2026-10-21.
- SMS tek ayar `Messaging:SmsEnabled` (pilotta kapalı). Panelde SMS ekranları `me.platform.smsEnabled`'a bakar; randevu bildirimi kanalı sadece `AppointmentNotifier` seçer (SMS → e-posta), WhatsApp mesajı POST ile önceden hazırlanır (`whatsapp-message`), dokunma kaydı keepalive (`whatsapp-notices`). E-posta sadece `EmailQueue` ile (OutboundEmails + Hangfire). Paket/limit sadece `IPlanAccess`.
- E2E iki çift: varsayılan SMS kapalı (5180/3100), başlığında `@sms` olan senaryolar SMS açık çiftte (5181/3101, "-sms" projeleri). Testte API/web adresi `e2e/support/target.ts`'ten (sabit `apiUrl` kullanma). Kodsuz online randevu E2E'si göndermeden 3,5 sn bekler (bot tuzağı).
- Renkler: ana düğme mavi + siyah yazı, koyu yüzey #000a1d, link/seçili #006ea6 (`text-link`; kapsama göre `text-link-text`). #00385a ve #00fdff sadece koyu zeminde. Logo sadece koyu zeminde (`Logo`), beyazda `Wordmark`. Kurallar `docs/04-tasarim.md`, test `contrast.test.ts`. Tanıtım sitesi (2026-10-21) ayrı token'lar: `ink` yazı, `slate` ikincil, `mist`/`cream` zemin, `coral` sıcak vurgu (beyazda yazı değil, yazıda `coral-text`); beyaz üst menüde `SiteBrand` (sadece yazı; KF işareti yalnız favicon); renk geçişi sadece ilk ekranda (`hero-glow`). Giriş/kayıt sol paneli `ink` zemin. Fotoğraflar `public/site/foto`, Pexels orijinalinden `npm run site:foto` (≥2x, WebP q85, `next/image` quality 85; kaynak `docs/foto-kaynaklari.md`). Panel logosu SVG (`logo-yazi.svg`). Özellikler `/ozellikler`.
- Şirket bilgisi `content/company.ts` + backend `Company:*`; hukuki metinler `content/legal.ts` (Kullanım koşulları/Veri işleme sözleşmesi sürümü değişince backend `LegalVersions` da). Site metinleri `content/site.ts`, fiyatlar `content/plans.ts`, yardım `content/help.ts`; demo görüntüleri `npm run demo:shots` (panel değişince yeniden), ikonlar `npm run marka`.
- Mesai sadece `TimeClock`, izin sadece `LeaveService` ile yazılır (personel satırı kilidi; açık mesai `UX_TimeEntries_Open`). Maaş personel yanıtında yok: `/api/staff/{id}/salary` (Sahip yazar, Sahip + Mali Müşavir görür). Onaylı izin = `StaffTimeOff` (Leave, LeaveRequestId); yıllık izin bakiyesi `LeaveBalanceEntries` toplamı. Slogan `content/site.ts` → `siteSlogan`.
- Prim sadece `CommissionService` ile ve sadece `SaleService` içinden (satışta tahakkuk, iptalde iptal ayına eksi kayıt; `Commissions` yalnızca eklenir). Kural seçimi Domain `CommissionRuleResolver`, tutar `CommissionCalculator` (taban `NetAmount`). Satır personeli `DocumentLineRequest.staffMemberId` (boş = personelsiz; varsayılanı istemci gönderir). Personel sadece kendi primini görür (`/api/commissions/me`).
- Para/miktar/gün/yüzde alanı sadece `NumberInput` (harf yok, virgül; `type="number"` yok). Hitap panel ve e-postada "siz", sitede "sen" (`hitap.test.ts`). İşletme adına giden e-postanın gönderen bilgisi `EmailSenders.For(business, app)` (ad, logo, telefon, adres). Vitest konsol hatası/uyarısında düşer; React dev uyarıları için tur `E2E_WEB_DEV=1`.
- Pencere/alt sayfa telefonda ilk alana odaklanmaz (`lib/focus.ts`, `DialogContent`/`SheetContent`); pencere içinde `autoFocus` gerekiyorsa `autoFocus={useIsDesktop()}`.
- Otomasyonlar `AutomationRunner`/`AutomationDispatcher` (anahtar başına bir gönderim: `AutomationSends`); pazarlama otomasyonları `MarketingSms` ile. Anket `/a/[kod]` (`SurveyEndpoints`). Ziyaret istatistiği sadece `ContactVisits.RecomputeAsync` ile (aynı gün tek ziyaret); risk `ContactQueries.AtRisk` (Domain `RiskRule` ile aynı).
- SMS kredisi sadece `SmsCredits` ile (atomik, eksiye düşmez); her SMS `ISmsSender`'dan (ücretli mesajda kredi düşer, hata iade). Pazarlama SMS'i: izin + çıkış eki + 09:00-21:00 + `MarketingDailySends` (günde 1). Kampanya gönderimi `CampaignSender`/`CampaignDispatcher` (alıcıya en fazla bir SMS; testte dağıtıcı `RunAsync(businessId)` ile sınırlanır).
- Çip/sekme satırları `components/ui/scroll-row.tsx` (`ScrollRow`, `Chip`, `TabNav`); telefonda Menü sağ üstte (`mobile-menu.tsx`).
- Randevu saati sadece `AppointmentBooking` ile yazılır (personel satırı kilidi + çakışma sorgusu); boş saat hesabı `Domain/Appointments/Availability`. Kilit sırası: personel → randevu → paket → numara → satış → cari → kasa → stok. Paket seansı sadece atomik UPDATE ile.
- SMS sadece `ISmsSender` ile (önce MessageLog); randevu SMS'leri `AppointmentNotifier`. `Sms:MirrorToEmail` (Mailpit yansıması) E2E/Development'ta açık, Production'da açılamaz.
- EF: alt sorguda `IgnoreQueryFilters([SoftDelete])` bütün sorgunun soft delete filtresini kapatır; silinebilen kayıtta adları ayrı sorguyla oku veya kökte `!IsDeleted` yaz.
- Bakiyeler (`Contact.Balance`, `CashAccount.Balance`) sadece `LedgerService` ile, stok (`Product.StockQuantity`) sadece `StockService` ile, SQL'de atomik güncellenir; entity üzerinden yazılmaz. Satış sadece `SaleService` ile yazılır (numara, cari, kasa, stok aynı transaction'da).
- Kasa hareketi silinmez: düzeltme `POST /api/cash-transactions/{id}/reverse` (ters kayıt, bugünün tarihiyle; tahsilat ve ödemede cari karşı kaydıyla). Toplamlarda ters kayıtlar düşülür (`CashTransaction.NetAmount`). Vade hesabı `ReceivableAllocator` (FIFO); "bugün" `BusinessTime` ile İstanbul günü.
- `proxy.ts` prefetch isteklerine girmez (matcher `missing`); cookie'siz segment prefetch'i `/giris`'e yönlenince 404 alıyordu.
- OpenAPI: yanıtta veya sorgu parametresinde nullable enum kullanma (ortak enum şemasını `| null` yapar, frontend tipleri bozulur); metin veya ayrı enum kullan.
- GitHub: github.com/kobifabrika/kobifabrika (private)
