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
| Test | xUnit (backend), Vitest + Testing Library (frontend), Playwright (uçtan uca, sonra) |

Mimari ayrıntılar: `docs/02-mimari.md`. Veri modeli: `docs/03-veri-modeli.md`.

## Klasör yapısı

```
kobifabrika/
├─ CLAUDE.md
├─ docker-compose.yml        # SQL Server + Mailpit (geliştirme)
├─ .env.example              # gizli değerlerin şablonu (.env git'e girmez)
├─ docs/                     # ürün, kapsam, mimari, veri modeli, tasarım, sprintler, karar günlüğü
├─ backend/
│  ├─ KobiFabrika.sln
│  ├─ src/KobiFabrika.Api/            # host, endpoint'ler (Features/<Modul>/), Program.cs
│  ├─ src/KobiFabrika.Domain/         # entity'ler, enum'lar, domain kuralları
│  ├─ src/KobiFabrika.Infrastructure/ # EF Core DbContext, migrations, SMS, e-posta, Hangfire işleri, PDF
│  └─ tests/KobiFabrika.Tests/
└─ frontend/                 # Next.js uygulaması
   └─ src/app/
      ├─ (site)/             # tanıtım sitesi: kobifabrika.com
      ├─ (auth)/             # giriş, kayıt, şifre sıfırlama
      ├─ (panel)/            # işletme paneli: app.kobifabrika.com
      └─ r/[slug]/           # işletmenin herkese açık online randevu sayfası
```

## Komutlar

```powershell
docker compose up -d                                  # SQL Server + Mailpit
dotnet run --project backend/src/KobiFabrika.Api      # API: http://localhost:5080
dotnet test backend
dotnet ef migrations add <Ad> -p backend/src/KobiFabrika.Infrastructure -s backend/src/KobiFabrika.Api
cd frontend; npm run dev                              # http://localhost:3000
cd frontend; npm run gen:api                          # OpenAPI'den TS tiplerini üret
cd frontend; npm run lint; npm run test
```

(Sprint 0 bu komutları kurar; değişirse bu bölümü güncelle.)

## Kurallar

- **Kod dili İngilizce, arayüz Türkçe.** Sınıf, tablo, değişken adları İngilizce (`Customer`, `Appointment`); kullanıcının gördüğü her metin Türkçe. Terim eşlemesi: `docs/03-veri-modeli.md` başındaki sözlük.
- **Çok kiracılı (multi-tenant):** İşletmeye ait her tabloda `BusinessId` var. EF Core global query filter ile otomatik süzülür; bir işletme başka işletmenin verisini asla göremez. Yeni tablo eklerken bunu unutma ve testini yaz.
- **Para:** `decimal(18,2)`, asla `float/double`. KDV oranları tabloda tutulur.
- **Zaman:** Veritabanında UTC (`datetime2`), arayüzde Europe/Istanbul.
- **Telefon:** `+90` ile E.164 biçiminde saklanır.
- **Silme:** Ana kayıtlarda soft delete (`IsDeleted`); her kayıtta `CreatedAt`, `CreatedBy`, `UpdatedAt`.
- **KVKK ve İYS:** SMS/e-posta izni olmayan müşteriye pazarlama mesajı gönderilmez. Her pazarlama mesajında çıkış seçeneği olur.
- **Yetki:** 4 rol: Owner (Sahip), Manager (Yönetici), Staff (Personel), Accountant (Mali Müşavir, salt okunur). Maaş bilgisini sadece Owner görür.
- **Gizli bilgiler** `.env` / user-secrets'ta; koda ve git'e asla girmez.
- **Tasarım:** Renkler ve kurallar `docs/04-tasarim.md`. Sadece 5 marka rengi + nötr griler. #00b0ff ve #00fdff beyaz zeminde yazı rengi olarak kullanılmaz.
- **Mobil:** En küçük yazı 12px, input yazısı 16px, dokunma alanı en az 44px. Her ekranı 375px genişlikte kontrol et.
- Küçük, okunur commit'ler; mesajlar Türkçe ve emir kipinde ("Müşteri listesini ekle").
- Bir özelliği bitirmeden önce: build geçer, testler geçer, lint temiz.

## Çalışma şekli

1. Her sprint başında `docs/05-sprintler.md`'deki ilgili sprinti oku, önce plan çıkar (plan modu), onaydan sonra kodla.
2. Bir karar alındığında `docs/karar-gunlugu.md`'ye tarihli satır ekle.
3. Sprint bitince `docs/05-sprintler.md`'de maddeleri işaretle ve "Durum" satırını güncelle.
4. Belirsiz bir ürün kararı varsa tahmin etme; sor.
5. Geliştirme ana makinesi Windows (PowerShell). Komutlar Mac'te de çalışmalı; satır sonları LF (`.gitattributes`).

## Güncel durum

- Sprint: **0 (kurulum)** başlamadı.
- GitHub hesabı henüz yok; şimdilik yerel git. Hesap açılınca `origin` eklenecek.
