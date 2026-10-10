# Mimari

## Genel bakış

```
Tarayıcı (telefon / bilgisayar)
   │
   ├─ kobifabrika.com, app.kobifabrika.com  →  Next.js (frontend)
   │                                             │  fetch (cookie ile)
   └─ api.kobifabrika.com  ←─────────────────────┘
          ASP.NET Core API (modüler monolit)
             ├─ SQL Server (tek veritabanı, BusinessId ile kiracı ayrımı)
             ├─ Hangfire (aynı veritabanında şeması: hatırlatma SMS, kampanya, yedekleme)
             ├─ SMS sağlayıcı (Netgsm / İleti Merkezi, İYS)
             ├─ E-posta (geliştirmede Mailpit, canlıda Brevo veya Resend)
             └─ iyzico (abonelik ödemesi)
```

Tek kişilik/küçük ekip için **modüler monolit**: tek deploy, tek veritabanı, ama kod modüllere ayrılmış. Mikroservis yok.

## Backend

### Projeler

- `KobiFabrika.Domain`: entity'ler, enum'lar, değer nesneleri, domain kuralları. Hiçbir altyapı bağımlılığı yok.
- `KobiFabrika.Infrastructure`: `AppDbContext` (EF Core 10, SQL Server), entity konfigürasyonları, migrations, Identity, Hangfire işleri, SMS/e-posta/PDF servisleri.
- `KobiFabrika.Api`: Program.cs, kimlik doğrulama, yetkilendirme, endpoint'ler. Endpoint'ler modül klasörlerinde:
  `Features/Auth`, `Features/Businesses`, `Features/Customers`, `Features/Catalog`, `Features/Sales`, `Features/Finance`, `Features/Appointments`, `Features/Marketing`, `Features/Staff`, `Features/Reports`, `Features/Billing`, `Features/Admin`, `Features/Public` (randevu sayfası, teklif onayı, tahsilat linki, memnuniyet anketi).
  Her özellik dosyası: istek/yanıt kayıtları, doğrulama (FluentValidation), handler ve `MapXxx` uzantısı.
- `KobiFabrika.Tests`: birim testleri + `WebApplicationFactory` ile entegrasyon testleri (Testcontainers ile SQL Server).

### Çok kiracılık (multi-tenancy)

- Tek veritabanı, her kiracı tablosunda `BusinessId` (Guid).
- Oturumdaki kullanıcının işletmesi claim'den okunur → `ITenantContext`.
- `AppDbContext` içinde iki adlı global query filter (EF Core 10): `ITenantScoped` için `Tenant` (`e.BusinessId == tenant.BusinessId`), `ISoftDeletable` için `SoftDelete` (`!e.IsDeleted`). Kiracı yoksa sorgu boş döner.
- Kayıt eklenirken `BusinessId` SaveChanges'te otomatik atanır; elle verilen farklı bir değer exception fırlatır.
- Herkese açık endpoint'ler (`/public/...`) kiracıyı slug veya imzalı token'dan çözer; oturum gerektirmez.
- **Zorunlu test:** her yeni modülde "A işletmesi B'nin kaydını göremez/değiştiremez" entegrasyon testi.

### Kimlik ve yetki

- ASP.NET Core Identity, httpOnly + Secure + SameSite=Lax cookie. Cookie domain canlıda `.kobifabrika.com` (app ve api alt alan adları paylaşır).
- Geliştirmede frontend `localhost:3000`, API `localhost:5080`; CORS `AllowCredentials` ile sadece frontend origin'i.
- Roller: `Owner`, `Manager`, `Staff`, `Accountant`. Yetki policy'leri endpoint gruplarında tanımlanır (`RequireRole`), alan bazlı kısıtlar (maaş) handler'da.
- Süper admin ayrı bir `PlatformAdmin` rolü. Kiracı filtresi rol bazında kapatılmaz; sadece `Features/Admin` endpoint'leri `IgnoreQueryFilters([QueryFilters.Tenant])` ile açıkça kapatır.
- Şifre sıfırlama ve e-posta doğrulama Identity token'ları ile.

### API kuralları

- REST, JSON, kebab-case yollar: `/api/customers`, `/api/appointments/{id}/complete`.
- Listeler sayfalı: `?page=1&pageSize=25&search=...`, yanıt `{ items, total }`.
- Hata yanıtları ProblemDetails (RFC 9457), doğrulama hataları Türkçe mesajlı.
- OpenAPI dokümanı `/openapi/v1.json`; geliştirmede Scalar arayüzü `/scalar`.
- Para alanları `decimal`, tarih alanları ISO 8601 UTC.

### Arka plan işleri (Hangfire)

- Randevu hatırlatma SMS'i (dakikalık tarama).
- Zamanlanmış kampanya gönderimi, doğum günü ve geri kazanma otomasyonları (günlük).
- Vade hatırlatmaları (günlük).
- Abonelik yenileme hatırlatması (7 gün önce), deneme bitişi.
- Gece yedekleme (SQL Server `BACKUP DATABASE`, Express'te SQL Agent olmadığı için).
- Hangfire paneli sadece PlatformAdmin'e açık.

### Dış servisler (arayüz + sahte uygulama)

Her dış servis bir arayüzün arkasında: `ISmsSender`, `IEmailSender`, `IPaymentProvider`, `IPdfRenderer`.
Geliştirmede sahte (fake) uygulamalar çalışır: SMS'ler veritabanındaki `MessageLog`'a ve konsola yazılır, e-postalar Mailpit'e gider. Gerçek sağlayıcılar pilot öncesi bağlanır.

### Loglama ve izleme

Serilog (konsol + dosya), canlıda Sentry (ücretsiz katman). Her istek için correlation id.

## Frontend

- Next.js App Router, TypeScript strict, Tailwind CSS, shadcn/ui bileşenleri, lucide-react ikonları.
- Route grupları: `(site)` tanıtım sitesi (SEO, statik), `(auth)`, `(panel)/panel` (oturum gerekli, istemci ağırlıklı; URL `/panel/...`, canlıda `app.kobifabrika.com` `proxy.ts` ile buraya yönlenir), `r/[slug]` randevu sayfası. Frontend URL'leri Türkçe.
- Veri çekme: `openapi-typescript` ile üretilen tipler + küçük bir `apiClient` (fetch, `credentials: "include"`). Sunucu durumu için TanStack Query.
- Formlar: react-hook-form + zod (backend doğrulamasıyla aynı kurallar).
- Tema: `docs/04-tasarim.md`'deki renkler Tailwind tema token'larına ve CSS değişkenlerine işlenir. Açık tema öncelikli; koyu tema sonra.
- Panel düzeni: masaüstünde koyu sol menü; telefonda alt sekme çubuğu (Bugün, Randevu, Satış, Müşteri, Menü) + hızlı işlem butonu.
- Erişilebilirlik: klavye ile kullanılabilir, görünür odak, form etiketleri.

## Geliştirme ortamı

- Windows 11 ana makine, Mac'te de çalışmalı. Satır sonları LF (`.gitattributes`).
- `docker-compose.yml`:
  - `sqlserver`: `mcr.microsoft.com/mssql/server:2025-latest`, port 1433, `MSSQL_PID=Developer`, SA şifresi `.env`'den, kalıcı volume.
  - `mailpit`: `axllent/mailpit`, SMTP 1025, arayüz http://localhost:8025.
- Bağlantı dizesi ve gizli değerler: `.env` (Docker için) ve `dotnet user-secrets` (API için). `.env.example` git'te.

## Canlı ortam (pilot öncesi)

Tek VPS (4-8 GB RAM), Docker Compose: API, Next.js, SQL Server Express, Caddy (otomatik HTTPS). GitHub Actions ile build ve deploy. Cloudflare DNS. Ayrıntılar Sprint 8'de.
