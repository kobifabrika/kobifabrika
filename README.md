# KobiFabrika

KOBİ ve esnafın parasını takip eden, müşteri getiren ve personelini yöneten tek uygulama.
Ürün ve mimari ayrıntıları: [`docs/`](docs/) · Claude Code talimatları: [`CLAUDE.md`](CLAUDE.md)

| Katman | Teknoloji |
|---|---|
| Backend | .NET 10 · ASP.NET Core Minimal API · EF Core 10 · SQL Server 2025 |
| Frontend | Next.js 16 (App Router) · TypeScript · Tailwind CSS 4 · shadcn/ui |
| Geliştirme servisleri | Docker: SQL Server 2025 + Mailpit |

## Gereksinimler

| Araç | Sürüm | Kontrol |
|---|---|---|
| Git | 2.40+ | `git --version` |
| Node.js | 22+ (LTS) | `node -v` |
| .NET SDK | 10.0.100+ | `dotnet --version` |
| Docker Desktop | güncel, "Engine running" | `docker version` |
| dotnet-ef | 10.x | `dotnet ef --version` |

### Windows'ta kurulum

```powershell
winget install --id Git.Git -e
winget install --id OpenJS.NodeJS.LTS -e
winget install --id Microsoft.DotNet.SDK.10 -e
winget install --id Docker.DockerDesktop -e
# Yeni bir PowerShell penceresinde:
dotnet tool install --global dotnet-ef
git config --global core.autocrlf false
```

Docker Desktop kurulduktan sonra bilgisayarı yeniden başlatın. WSL 2 isterse onaylayın (`wsl --install`).

### Mac'te kurulum

```bash
brew install git node@22
brew install --cask dotnet-sdk docker
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"   # ~/.zshrc dosyasına ekleyin
```

> **Apple Silicon (M serisi):** SQL Server imajı sadece amd64 olarak yayımlanıyor. Docker Desktop → Settings → General →
> "Use Rosetta for x86_64/amd64 emulation" seçeneğini açın. `docker-compose.yml` bu yüzden `platform: linux/amd64` kullanır.

## İlk çalıştırma

Komutlar repo kök klasöründe çalıştırılır. Windows (PowerShell) ve Mac (zsh) için aynıdır; sadece kopyalama komutu farklıdır.

**1. Gizli değerler.** `.env` dosyasını oluşturun ve `MSSQL_SA_PASSWORD` değerini değiştirin. Şifrede en az 8 karakter,
büyük harf, küçük harf, rakam ve sembol olmalı.

```powershell
Copy-Item .env.example .env      # Windows
```
```bash
cp .env.example .env             # Mac
```

**2. SQL Server ve Mailpit'i başlatın.** İlk seferde imaj indirildiği için birkaç dakika sürer.

```bash
docker compose up -d
docker compose ps                # sqlserver satırında "(healthy)" görünmeli
```

**3. API bağlantı dizesi (user-secrets).** `<SIFRE>` yerine `.env`'deki şifreyi yazın:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=KobiFabrika;User Id=sa;Password=<SIFRE>;TrustServerCertificate=True" --project backend/src/KobiFabrika.Api
```

> PowerShell'de şifrede `$` varsa bağlantı dizesini tek tırnakla yazın.

**4. Veritabanını oluşturun.**

```bash
dotnet ef database update -p backend/src/KobiFabrika.Infrastructure -s backend/src/KobiFabrika.Api
```

**5. API'yi çalıştırın** (ayrı bir terminalde açık kalsın):

```bash
dotnet run --project backend/src/KobiFabrika.Api
```

**6. Frontend'i çalıştırın** (başka bir terminalde):

```bash
cd frontend
npm install
npm run dev
```

## Hızlı başlatma (Windows): `baslat.ps1` ve `durdur.ps1`

İlk çalıştırmanın 1. (`.env`) ve 3. (user-secrets) adımlarını bir kez yaptıktan sonra her gün tek komut yeter.
Repo kök klasöründe PowerShell'de:

```powershell
.\baslat.ps1        # Docker kontrolü → docker compose up -d → migration'lar → API ve frontend ayrı pencerelerde → tarayıcı
.\durdur.ps1        # API ve frontend pencerelerini kapatır, SQL Server ve Mailpit'i durdurur (veriler silinmez)
```

`baslat.ps1` sırasıyla şunları yapar:

1. Araçları (`docker`, `dotnet`, `dotnet-ef`, `npm`), `.env` dosyasını ve API bağlantı dizesini kontrol eder; eksik varsa ne yapılacağını yazıp durur.
2. Docker çalışmıyorsa Docker Desktop'ı açar ve hazır olmasını bekler.
3. `docker compose up -d` çalıştırır, SQL Server "healthy" olana kadar bekler.
4. Migration'ları uygular (`dotnet ef database update`).
5. `frontend/node_modules` yoksa `npm install` yapar.
6. API'yi (**KobiFabrika API**) ve frontend'i (**KobiFabrika Frontend**) iki ayrı PowerShell penceresinde başlatır. Daha önce
   kendi açtığı pencere hâlâ açık ve yanıt veriyorsa onu tekrar açmaz. Portta sahipsiz kalmış bir `dotnet`/`node` süreci varsa
   (kapatılmış bir terminalden veya başka bir araçtan kalan) onu durdurup yeniden başlatır.
7. http://localhost:5080/health ve http://localhost:3000 hazır olunca tarayıcıda http://localhost:3000 adresini açar.

| Seçenek | Ne yapar |
|---|---|
| `.\baslat.ps1 -TarayiciAcma` | Her şeyi başlatır ama tarayıcıyı açmaz. |
| `.\baslat.ps1 -Hizli` | Frontend'i production modunda açar (`npm run build` + `npm start`). İlk açılış 1-3 dakika sürer, sonra sayfalar anında açılır; elle deneme için. Kod değişikliği otomatik yansımaz: tekrar `.\baslat.ps1 -Hizli`. Dev moduna dönmek için `.\baslat.ps1`. |
| `.\durdur.ps1 -DockerKalsin` | Sadece API ve frontend'i kapatır; SQL Server ve Mailpit açık kalır. |

- Açılan pencerelerin PID'leri `.baslat.pids` dosyasında tutulur (git dışı). `durdur.ps1` ayrıca 5080 ve 3000 portlarında kalan
  `dotnet`/`node` süreçlerini de kapatır; bu portlarda başka bir program varsa ona dokunmaz, sadece uyarır.
- Betik çalışmıyorsa ("running scripts is disabled") bir kez şunu çalıştırın:
  `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`
- Migration hatası alırsanız: API penceresi açıksa Windows DLL'leri kilitler. Betik bu durumda derlemeden (`--no-build`) uygular;
  yeni bir migration eklediyseniz önce `.\durdur.ps1 -DockerKalsin`, sonra `.\baslat.ps1`.
- Mac'te bu betikler yok; "İlk çalıştırma" bölümündeki 2, 4, 5 ve 6. adımları kullanın.

## Adresler

| Adres | Ne |
|---|---|
| http://localhost:3000 | Tanıtım sitesi (taslak) |
| http://localhost:3000/giris | Giriş ekranı |
| http://localhost:3000/panel | İşletme paneli iskeleti (sol altta "API bağlı" yazmalı) |
| http://localhost:5080/scalar | API dokümanı (Scalar) |
| http://localhost:5080/health | Sağlık kontrolü (`Healthy`) |
| http://localhost:5080/openapi/v1.json | OpenAPI dokümanı |
| http://localhost:8025 | Mailpit (geliştirmede giden e-postalar) |

Telefon görünümü: tarayıcıda F12 → cihaz simgesi → iPhone SE (375px).

## Testler: tek komut `test.ps1`

Bütün testler tek komutla, elle tıklamadan çalışır. Docker açık olmalı (kapalıysa Windows'ta Docker Desktop'ı kendisi açar).

```powershell
.\test.ps1              # Hepsi: Docker kontrolü → backend → frontend lint, birim, build → uçtan uca (E2E). ~5 dk
.\test.ps1 -Hizli       # Sadece birim testleri (backend + frontend). ~2 dk
.\test.ps1 -E2E         # Sadece uçtan uca testler
.\test.ps1 -E2E -Gorunur   # Tarayıcı görünür ve yavaş; testleri izlemek için
.\test.ps1 -Rapor       # Bitince Playwright HTML raporunu açar (her ekranın masaüstü ve 375 px görüntüsü)
```

Mac'te: `pwsh ./test.ps1` (PowerShell: `brew install powershell`).

Sonunda Türkçe bir tablo çıkar: bölüm, geçen, kalan, süre. Kalan test varsa adı, kısa nedeni ve ekran görüntüsünün yolu
yazılır; çıkış kodu 1 olur. Hata olan testin trace'i ve videosu `frontend/playwright-report`'ta (`-Rapor` ile açılır).

**Uçtan uca (E2E) testler** `frontend/e2e/` altında (Playwright). Geliştirme ortamından tamamen ayrıdır, `baslat.ps1` açıkken
de çalışır:

| | Geliştirme | E2E |
|---|---|---|
| API | http://localhost:5080 | http://localhost:5180 (Release, ortam `E2E`) |
| Frontend | http://localhost:3000 (`.next`) | http://localhost:3100 (production build, `.next-e2e`) |
| Veritabanı | `KobiFabrika` | `KobiFabrika_E2E` (her çalıştırmada silinip migration'larla kurulur) |
| E-posta | Mailpit | Aynı Mailpit; her test kendi `e2e+…@example.test` adresini kullanır |

- Projeler: masaüstü Chromium (1280 px), telefon 375 px (Chromium ve WebKit).
- Her ekranda kendiliğinden: konsol hatası, beklenmeyen 4xx/5xx, 375 px'te yatay taşma ve axe-core ciddi/kritik
  erişilebilirlik hatası (renk kontrastı dahil) testi düşürür; 3 saniyeden geç açılan sayfa uyarı verir.
- Test verisi API üzerinden hazırlanır (`e2e/support/api.ts`); sadece test için açılmış bir uç yoktur.
- Elle çalıştırma (frontend klasöründe): `npx playwright test`, tek dosya: `npx playwright test e2e/sprint3-satis.spec.ts`,
  tek proje: `--project=masaustu`. Test yazarken sunucular açık kalsın diye: `node e2e/scripts/start-api.mjs` ve
  `node e2e/scripts/start-web.mjs` ayrı pencerelerde, sonra `$env:E2E_REUSE=1; npx playwright test`.
- GitHub'da her push ve PR'da aynı testler çalışır (`.github/workflows/test.yml`); rapor "Artifacts" altında.

Yeni bir ekran eklendiğinde E2E senaryosu da eklenir (CLAUDE.md → Bitti tanımı).

## Lint, tip üretimi (tek tek)

```bash
dotnet test backend              # Docker açık olmalı: Testcontainers kendi SQL Server konteynerini açar
cd frontend
npm run lint
npm run test
npm run build
npm run gen:api                  # API çalışırken: OpenAPI'den src/lib/api/schema.d.ts üretir
```

Backend'de bir endpoint ekleyip değiştirdikten sonra `npm run gen:api` çalıştırın ve üretilen dosyayı commit'leyin.

## Yeni migration

```bash
dotnet ef migrations add <Ad> -p backend/src/KobiFabrika.Infrastructure -s backend/src/KobiFabrika.Api -o Persistence/Migrations
dotnet ef database update -p backend/src/KobiFabrika.Infrastructure -s backend/src/KobiFabrika.Api
```

## Sorun giderme

- **`ConnectionStrings:Default bulunamadı`:** 3. adımı uygulayın. User-secrets sadece `Development` ortamında okunur.
- **SQL Server "unhealthy" veya hemen kapanıyor:** Şifre karmaşıklık kuralını sağlamıyor olabilir. `docker compose logs sqlserver`
  ile bakın, `.env`'i düzeltin, `docker compose down -v` ile volume'u silip tekrar başlatın (veritabanı silinir).
- **Port dolu (1433, 5080, 3000):** Başka bir SQL Server veya uygulama çalışıyor olabilir. `.env`'de `MSSQL_PORT`'u değiştirip
  bağlantı dizesindeki portu da güncelleyin.
- **Windows'ta API derlenmiyor ("dosya kullanımda"):** Çalışan API'yi durdurun (Ctrl+C), sonra tekrar derleyin.
- **Panelde sayfalar dakikalarca "Rendering" diyor, konsolda "Failed to fetch RSC payload … TypeError: Failed to fetch":**
  Next.js dev sunucusu bozulmuş; çoğunlukla çıktısını okuyan terminal kapanmış bir süreçtir (dev logunda `write EPIPE`,
  `Jest worker encountered 2 child process exceptions`). `.\durdur.ps1 -DockerKalsin` ve `.\baslat.ps1` ile yeniden başlatın.
  Dev sunucusunu kapanacak bir terminalde arka planda başlatmayın.
