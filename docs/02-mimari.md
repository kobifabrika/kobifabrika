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
- Herkese açık endpoint'ler (`/api/public/...`) kiracıyı slug veya token'dan çözer; oturum gerektirmez. Token: 32 bayt rastgele (Base64Url), veritabanında SHA-256 özeti (`SecureTokens`); süreli ve iptal edilebilir. Kayıt `IgnoreQueryFilters([Tenant])` ile token özetinden bulunur, işlem o işletmenin kiracı bağlamındaki yeni DI scope'unda yapılır. Yanıtlar `X-Robots-Tag: noindex`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store`; IP başına dakikada sınır (`RateLimiting:PublicPermitLimit`).
- **Bakiye eşzamanlılığı:** `Contact.Balance` ve `CashAccount.Balance` önbellektir ve SaveChanges'te yazılmaz (`PropertySaveBehavior.Ignore`). `LedgerService` hareketi ve atomik `UPDATE … SET Balance = Balance + @delta`'yı aynı transaction'da yazar; kilit sırası tahsilat linki → cari → kasa. Tahsilat linki onayı koşullu UPDATE (`Status IN (Open, ClaimedPaid)`) ile tek kez yapılır.
- **Zorunlu test:** her yeni modülde "A işletmesi B'nin kaydını göremez/değiştiremez" entegrasyon testi.

### Kimlik ve yetki

- ASP.NET Core Identity, httpOnly + Secure + SameSite=Lax cookie (`kf.auth`). Cookie domain canlıda `.kobifabrika.com` (app ve api alt alan adları paylaşır).
- "Beni hatırla" işaretliyse kalıcı cookie (30 gün, kayan süre), değilse oturum cookie'si. Security stamp 5 dakikada bir kontrol edilir: şifre sıfırlama/değiştirme diğer oturumları düşürür.
- E-posta doğrulanmadan giriş yapılır (`RequireConfirmedEmail = false`); davet gibi işlemler `VerifiedEmail` policy'si ister.
- Geliştirmede frontend `localhost:3000`, API `localhost:5080`; CORS `AllowCredentials` ile sadece frontend origin'i.
- Cookie'de sadece kullanıcı ve aktif işletme (`kf:business_id`, `AppUser.LastBusinessId`'den) var; **rol cookie'de tutulmaz.** `TenantResolutionMiddleware` her istekte (UserId, BusinessId) üyeliğini tek sorguyla doğrular, `TenantContext` ve `CurrentMember`'ı (rol, e-posta doğrulandı mı, kurulum bitti mi) doldurur. Üyelik yok/pasifse kiracı bağlamı boş kalır (fail-closed). Rol değişikliği ve ekipten çıkarma bir sonraki istekte geçerli olur.
- Roller: `Owner`, `Manager`, `Staff`, `Accountant`. Policy'ler (`Api/Common/Authorization/Policies.cs`): `Panel` (her üye, kurulum bitmiş), `CanWrite` (Accountant hariç), `ViewSettings` (Owner, Manager, Accountant), `ManageBusiness` ve `ManageTeam` (Owner, Manager), `ViewAuditLog` (Owner, Manager, Accountant), `OwnerOnly`, `Setup` (işletmesi olmayan veya kurulumu bitmemiş Owner), `VerifiedEmail`. Ret yanıtı Türkçe ProblemDetails + `code` (ör. `setup_required`, `email_not_confirmed`); frontend yönlendirmeyi buna göre yapar. Alan bazlı kısıtlar (maaş) ve "Owner rolüne sadece Owner dokunur" gibi kurallar handler'da.
- Frontend'deki `src/lib/permissions.ts` aynı matrisi sadece menü/buton görünürlüğü için tutar.
- Süper admin ayrı bir `PlatformAdmin` rolü. Kiracı filtresi rol bazında kapatılmaz; sadece `Features/Admin` endpoint'leri `IgnoreQueryFilters([QueryFilters.Tenant])` ile açıkça kapatır.
- Herkese açık davet uçları (`/api/invitations/{token}`) kiracıyı token özetinden çözer; kabul işlemi davetin işletmesine ait ayrı bir DI scope'unda yapılır (oturumdaki kullanıcının başka aktif işletmesi olsa da kiracı bağlamı karışmaz).
- Şifre sıfırlama (2 saat) ve e-posta doğrulama (1 gün) Identity token'larıyla; link token'ları Base64Url. Kayıt, giriş ve şifre uçlarında IP başına dakikada sınır (`RateLimiting:AuthPermitLimit`), doğrulama e-postası kullanıcı başına 60 sn'de bir.
- Şifre kuralı: en az 8 karakter, bir harf, bir rakam; Identity hataları Türkçe (`TurkishIdentityErrorDescriber`).

### API kuralları

- REST, JSON, kebab-case yollar: `/api/customers`, `/api/appointments/{id}/complete`.
- Listeler sayfalı: `?page=1&pageSize=25&search=...`, yanıt `{ items, total }`.
- Hata yanıtları ProblemDetails (RFC 9457), doğrulama hataları Türkçe mesajlı.
- OpenAPI dokümanı `/openapi/v1.json`; geliştirmede Scalar arayüzü `/scalar`.
- Para alanları `decimal`, tarih alanları ISO 8601 UTC.
- JSON: enum'lar metin (`"Owner"`), sayılar katı (`JsonNumberHandling.Strict`; OpenAPI'de `integer`). Doğrulama FluentValidation ile (`.Validate<T>()` uç filtresi), alan adları camelCase.

### Arka plan işleri (Hangfire)

Sprint 2'de kuruldu (`Infrastructure/Jobs/BackgroundJobs.cs`): SQL Server depolama, aynı veritabanında `hangfire` şeması, 2 worker. Testlerde `Hangfire:Enabled=false`; iş sınıfları doğrudan çağrılarak test edilir. İşler kiracıdan bağımsız başlar, her işletme için ayrı DI scope'unda `TenantContext.Set` ile çalışır. Şu an: `reminders-due` (dakikada bir, vakti gelen hatırlatmaya bildirim), `import-jobs-cleanup` (saatlik). Hangfire paneli açık değil (PlatformAdmin, Sprint 8).

- Randevu hatırlatma SMS'i (dakikalık tarama).
- Zamanlanmış kampanya gönderimi, doğum günü ve geri kazanma otomasyonları (günlük).
- Vade hatırlatmaları (günlük).
- Abonelik yenileme hatırlatması (7 gün önce), deneme bitişi.
- Gece yedekleme (SQL Server `BACKUP DATABASE`, Express'te SQL Agent olmadığı için).
- Hangfire paneli sadece PlatformAdmin'e açık.

### Dış servisler (arayüz + sahte uygulama)

Her dış servis bir arayüzün arkasında: `ISmsSender`, `IEmailSender`, `IPaymentProvider`, `IPdfRenderer` (Sprint 2: QuestPDF Community lisansı, gömülü Lato fontu Türkçe karakterleri destekler). Excel okuma/yazma ClosedXML ile.
Geliştirmede sahte (fake) uygulamalar çalışır: SMS'ler veritabanındaki `MessageLog`'a ve konsola yazılır, e-postalar MailKit ile SMTP üzerinden Mailpit'e gider (`Email:Smtp`). Gerçek sağlayıcılar pilot öncesi bağlanır.
E-posta Sprint 1'de istek içinde gönderilir (hata loglanır, istek düşmez); Hangfire gelince (Sprint 5) kuyruğa alınacak.
Dosyalar (logo) `IFileStorage` arkasında: geliştirmede yerel disk (`Storage:LocalPath`, varsayılan `App_Data/files`, git dışı), API'den `/files/...` altında sunulur.

### Loglama ve izleme

Serilog (konsol + dosya), canlıda Sentry (ücretsiz katman). Her istek için correlation id.

## Frontend

- Next.js App Router, TypeScript strict, Tailwind CSS, shadcn/ui bileşenleri, lucide-react ikonları.
- Route grupları: `(site)` tanıtım sitesi (SEO, statik), `(auth)`, `(panel)/panel` (oturum gerekli, istemci ağırlıklı; URL `/panel/...`, canlıda `app.kobifabrika.com` `proxy.ts` ile buraya yönlenir), `r/[slug]` randevu sayfası, `ekstre/[token]` ve `odeme/[token]` (işletmenin müşterisinin göreceği herkese açık sayfalar; noindex). `(auth)` grubunda `/giris`, `/kayit`, `/sifremi-unuttum`, `/sifre-sifirla`, `/e-posta-dogrula`, `/davet/[token]`; kurulum sihirbazı `/kurulum`. Frontend URL'leri Türkçe.
- Oturum: `src/proxy.ts` sadece `kf.auth` cookie'sinin varlığına bakar (yoksa `/giris?donus=…`). Asıl kontrol `PanelGate`'te: `GET /api/auth/me` 401 → giriş, kurulum bitmemiş → `/kurulum`. Panel içeriği istemcide render edildiği için panel segmentleri `export const instant = false` ile anında gezinme doğrulamasının dışında.
- Veri çekme: `openapi-typescript` ile üretilen tipler + küçük bir `apiClient` (fetch, `credentials: "include"`). Sunucu durumu için TanStack Query.
- Formlar: react-hook-form + zod (backend doğrulamasıyla aynı kurallar).
- Tema: `docs/04-tasarim.md`'deki renkler Tailwind tema token'larına ve CSS değişkenlerine işlenir. Beyaz tema varsayılan; siyah kapsam (`.dark` sınıfı) panelin üst çubuğu ve sitedeki siyah bölümler için.
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
