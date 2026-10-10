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

## Test, lint, tip üretimi

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
