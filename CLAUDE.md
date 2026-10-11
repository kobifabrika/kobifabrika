# KobiFabrika — Claude Code proje talimatları

Bu dosya Claude Code'un her oturumda okuduğu proje hafızasıdır. Kısa tut; ayrıntılar `docs/` klasöründe.

## Ürün

KobiFabrika, Türkiye'deki küçük ve orta ölçekli işletmeler ve esnaf için web tabanlı (SaaS) işletme yazılımıdır.
Tek uygulamada hem parayı takip eder (cari, kasa, veresiye, stok, satış, teklif) hem müşteri getirir
(randevu, SMS kampanyası, Google yorum, geri kazanma) hem de personeli yönetir (mesai, izin, prim).

- Şirket: KobiFabrika Yazılım A.Ş. · Ürün adı her yerde **KobiFabrika** (eski çalışma adı KOBIOS; kodda, arayüzde, metinlerde KOBIOS geçmeyecek).
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
- **Tasarım:** Renkler ve kurallar `docs/04-tasarim.md`. Sadece 3 renk: siyah, beyaz, vurgu mavisi #00b0ff (+ renksiz griler; kırmızı sadece hata/silme). Mavi beyaz zeminde yazı rengi olarak kullanılmaz; gradyan, gölgeli kart, emoji yok.
- **URL'ler:** Frontend yolları Türkçe (`/panel/musteriler`, `/giris`), API yolları İngilizce (`/api/customers`).
- **Mobil:** En küçük yazı 12px, input yazısı 16px, dokunma alanı en az 44px. Her ekranı 375px genişlikte kontrol et.
- Küçük, okunur commit'ler; mesajlar Türkçe ve emir kipinde ("Müşteri listesini ekle").
- **Bitti tanımı:** Bir özellik ya da sprint ancak `.\test.ps1` tamamen yeşilse (backend, frontend lint/birim/build, E2E) ve
  yeni her ekranın E2E senaryosu varsa (`frontend/e2e/`, masaüstü + 375 px) bitmiş sayılır. E2E gerçek bir hata bulursa
  test gevşetilmez, uygulama düzeltilir.

## Çalışma şekli

1. Her sprint başında `docs/05-sprintler.md`'deki ilgili sprinti oku, önce plan çıkar (plan modu), onaydan sonra kodla.
2. Bir karar alındığında `docs/karar-gunlugu.md`'ye tarihli satır ekle.
3. Sprint bitince `docs/05-sprintler.md`'de maddeleri işaretle ve "Durum" satırını güncelle.
4. Belirsiz bir ürün kararı varsa tahmin etme; sor.
5. Geliştirme ana makinesi Windows (PowerShell). Komutlar Mac'te de çalışmalı; satır sonları LF (`.gitattributes`).
6. Her sprint sonunda ve her büyük adımdan sonra commit at ve git push ile GitHub'a gönder (origin: github.com/kobifabrika/kobifabrika). Silme veya geri alınamaz bir işlem yapmadan önce commit at.

## Güncel durum

- Sprint: **3 (Ürün, satış, teklif)** tamamlandı (2026-10-11): katalog, KDV, Excel'den ürün aktarma, stok hareketleri ve kritik stok, hızlı satış (barkod, bölünmüş ödeme, veresiye → cari), 80 mm fiş, iptal (ters kayıt), teklif linki ve satışa dönüştürme. Ardından (2026-10-11) otomatik test altyapısı: `test.ps1`, Playwright E2E (69 senaryo-proje), GitHub Actions; teklif indirim türü, süresi dolmuş teklif onayı, 58/80 mm fiş. Sıradaki: **Sprint 4 (Finans ve dashboard)**.
- Bakiyeler (`Contact.Balance`, `CashAccount.Balance`) sadece `LedgerService` ile, stok (`Product.StockQuantity`) sadece `StockService` ile, SQL'de atomik güncellenir; entity üzerinden yazılmaz. Satış sadece `SaleService` ile yazılır (numara, cari, kasa, stok aynı transaction'da).
- GitHub: github.com/kobifabrika/kobifabrika (private)
