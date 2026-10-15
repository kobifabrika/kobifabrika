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
- `AppDbContext` içinde iki adlı global query filter (EF Core 10): `ITenantScoped` için `Tenant` (`e.BusinessId == tenant.BusinessId`), `ISoftDeletable` için `SoftDelete` (`!e.IsDeleted`). Kiracı yoksa sorgu boş döner. **Dikkat (5B'de bulundu):** `IgnoreQueryFilters([SoftDelete])` bir alt sorguda (ör. `Select` içinde silinmiş müşterinin adı) kullanılınca bütün sorgunun soft delete filtresini kapatır; silinebilen ana kayıtta ad ayrı sorguyla okunur veya kökte `!IsDeleted` açıkça yazılır (teklif listesi düzeltildi).
- Kayıt eklenirken `BusinessId` SaveChanges'te otomatik atanır; elle verilen farklı bir değer exception fırlatır.
- Herkese açık endpoint'ler (`/api/public/...`) kiracıyı slug veya token'dan çözer; oturum gerektirmez. Token: 32 bayt rastgele (Base64Url), veritabanında SHA-256 özeti (`SecureTokens`); süreli ve iptal edilebilir. Kayıt `IgnoreQueryFilters([Tenant])` ile token özetinden bulunur, işlem o işletmenin kiracı bağlamındaki yeni DI scope'unda yapılır. Yanıtlar `X-Robots-Tag: noindex`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store`; IP başına dakikada sınır (`RateLimiting:PublicPermitLimit`).
- **Anket (Sprint 6B):** `/api/public/surveys/{code}` (GET, `/score`, `/comment`, `/google-click`); kod 10 karakter, SHA-256 özeti global benzersiz; kiracı koddan çözülür, işlem o işletmenin scope'unda; puan koşullu UPDATE ile tek kez (paralelde tek kabul), 7 gün sonra 410; 1-3 puan ve yorum Sahip/Yöneticiye bildirim. Frontend `/a/[kod]`.
- **Çıkış linki (Sprint 6A):** `/api/public/opt-out/{code}` (GET durum, POST "SMS'leri durdur"); kod müşteriye özel 8 karakter (bütün işletmelerde benzersiz), kiracı koddan çözülür; koşullu UPDATE ile izin bir kez kapanır (kaynak `OptOutLink`, İYS Pending), işletmeye bildirim. Link kökü `PublicLinks:ShortBaseUrl` (geliştirmede http://localhost:3000, canlıda kobifabrika.com); SMS'teki ek ASCII: "SMS almamak icin: {kök}/c/{kod} veya RET" (`Marketing:OptOutKeyword`).
- **Online randevu uçları (Sprint 5B, `Features/Public/PublicBookingEndpoints.cs`):** `/api/public/booking/{slug}` (sayfa), `/slots` (boş saatler), `/verifications` (SMS kodu), `/appointments` (kodu doğrula + randevu) ve `/api/public/appointments/{token}` (+ `/cancel`). İşletme slug'dan çözülür; işletme yoksa, Randevular modülü veya online randevu kapalıysa 404. Hız sınırları: `booking-read` IP başına dakikada 120 (`RateLimiting:BookingReadPermitLimit`), `booking-sms` IP başına 10 dakikada 5 (`RateLimiting:BookingSmsPermitLimit`), randevu ve iptal `public-links`; ayrıca numara başına 60 sn'de 1 ve saatte 3 kod (bütün işletmelerde, veritabanından) ve işletme başına günlük doğrulama SMS üst sınırı (`Sms:VerificationDailyLimit`, varsayılan 100). IP sınırları canlıda Caddy arkasında forwarded headers ile gerçek istemci IP'sini görmeli (Sprint 8). "Fark etmez" seçiminde o saatte boş personeller o gün en az dolu dakikaya göre sıralanır; her aday ayrı transaction'da denenir (kilit tutulup sıradakine geçilmez). Kod: 6 hane, SHA-256(kod + Id), 5 dk, 3 yanlış deneme, tek kullanım; doğrulanmış kod randevuya dönüşmediyse 15 dk içinde başka saatle kullanılır. Telefon silinmiş (KVKK) kayda asla bağlanmaz: soft delete filtresiyle aranır, yoksa yeni müşteri açılır.
- **Bakiye eşzamanlılığı:** `Contact.Balance` ve `CashAccount.Balance` önbellektir ve SaveChanges'te yazılmaz (`PropertySaveBehavior.Ignore`). `LedgerService` hareketi ve atomik `UPDATE … SET Balance = Balance + @delta`'yı aynı transaction'da yazar; kilit sırası ters kaydı yapılan hareket / tahsilat linki → cari → kasa (birden çok kasa hesabı Id sırasıyla: A→B ve B→A transferleri kilitlenmez). Kasa güncellemesi `IsActive` şartlı: pasife alma ile eşzamanlı hareket ya hesabı aktif ya da pasif + bakiye 0 bırakır (`ConflictException` → 409 `account_inactive`). Tahsilat linki onayı koşullu UPDATE (`Status IN (Open, ClaimedPaid)`) ile tek kez yapılır. Kasa işlemleri (transfer, cariye ödeme, ters kayıt) `Features/Finance/CashOperations.cs`.
- **Randevu eşzamanlılığı (Sprint 5A):** randevu saatinin tek yazıldığı yer `Features/Appointments/AppointmentBooking.cs`: transaction içinde personel satırı `UPDLOCK, HOLDLOCK` ile kilitlenir, sonra çakışma sorgusu ve kayıt. Aynı personele eşzamanlı istekler sıraya girer; farklı personeller beklemez. Satışa çevirmede randevu satırı `SaleService` içinde ilk adımda `UPDLOCK` ile kilitlenir (tek çevirme). Kilit sırası: personel → randevu → paket → numara → satış → cari → kasa → stok. Paket seansı sadece atomik UPDATE ile düşer/iade edilir (`UsedSessions = UsedSessions + 1 … WHERE UsedSessions < TotalSessions`); paketli satış iptali önce paketleri kilitler. Boş saat/çalışma saati hesabı saf fonksiyon `Domain/Appointments/Availability.cs` (online pencere `BookingWindow`).
- **SMS kredisi (Sprint 6A):** `Businesses.SmsBalance` sadece `Infrastructure/Messaging/SmsCredits` ile: düşüm `UPDATE … SET SmsBalance = SmsBalance - @n OUTPUT … WHERE SmsBalance >= @n` (yetmezse 0 satır → `NoCredit`, bakiye eksiye düşmez; CHECK ayrıca), hareket aynı transaction'da `SmsCreditTransactions`'a. `ISmsSender` ücretli mesajda log + düşümü tek kısa transaction'da yazar, sonra sağlayıcı; hata olursa iade (bir kez). İşletme satırı sadece bu kısa transaction'da kilitlenir, başka kilitle birlikte tutulmaz (SMS'ler randevu/satış commit'inden sonra). Test: bakiye 50'ken 80 paralel SMS → 50 Sent, 30 NoCredit, bakiye 0.
- **Kampanya gönderimi (Sprint 6A):** `Features/Marketing/CampaignSender` + `CampaignDispatcher`. Alıcı başına koşullu UPDATE `Pending → Sending` (kampanya hâlâ Sending ise), günlük pay satırı (`MarketingDailySends` benzersiz), SMS (`RefType = CampaignRecipient`, filtreli benzersiz indeks). "Sending"de kalan alıcı yeniden gönderilmez; `Marketing:StaleMinutes` (10) sonra `Failed (Interrupted)`. Pencere `Marketing:SendWindowStart/End` (09:00-21:00; E2E'de 00:00-24:00).
- **Zorunlu test:** her yeni modülde "A işletmesi B'nin kaydını göremez/değiştiremez" entegrasyon testi.

### Kimlik ve yetki

- ASP.NET Core Identity, httpOnly + Secure + SameSite=Lax cookie (`kf.auth`). Cookie domain canlıda `.kobifabrika.com` (app ve api alt alan adları paylaşır).
- "Beni hatırla" işaretliyse kalıcı cookie (30 gün, kayan süre), değilse oturum cookie'si. Security stamp 5 dakikada bir kontrol edilir: şifre sıfırlama/değiştirme diğer oturumları düşürür.
- E-posta doğrulanmadan giriş yapılır (`RequireConfirmedEmail = false`); davet gibi işlemler `VerifiedEmail` policy'si ister.
- Geliştirmede frontend `localhost:3000`, API `localhost:5080`; CORS `AllowCredentials` ile sadece frontend origin'i.
- Cookie'de sadece kullanıcı ve aktif işletme (`kf:business_id`, `AppUser.LastBusinessId`'den) var; **rol cookie'de tutulmaz.** `TenantResolutionMiddleware` her istekte (UserId, BusinessId) üyeliğini tek sorguyla doğrular, `TenantContext` ve `CurrentMember`'ı (rol, e-posta doğrulandı mı, kurulum bitti mi) doldurur. Üyelik yok/pasifse kiracı bağlamı boş kalır (fail-closed). Rol değişikliği ve ekipten çıkarma bir sonraki istekte geçerli olur.
- Roller: `Owner`, `Manager`, `Staff`, `Accountant`. Policy'ler (`Api/Common/Authorization/Policies.cs`): `Panel` (her üye, kurulum bitmiş), `CanWrite` (Accountant hariç), `ViewSettings` (Owner, Manager, Accountant), `ManageBusiness` ve `ManageTeam` (Owner, Manager), `ViewAuditLog` (Owner, Manager, Accountant), `OwnerOnly`, `Setup` (işletmesi olmayan veya kurulumu bitmemiş Owner), `VerifiedEmail`. Ret yanıtı Türkçe ProblemDetails + `code` (ör. `setup_required`, `email_not_confirmed`); frontend yönlendirmeyi buna göre yapar. Alan bazlı kısıtlar (maaş) ve "Owner rolüne sadece Owner dokunur" gibi kurallar handler'da.
- Frontend'deki `src/lib/permissions.ts` aynı matrisi sadece menü/buton görünürlüğü için tutar.
- Randevu (Sprint 5A): her rol takvimi görür; Sahip, Yönetici, Personel yazar (`CanWrite`); personel, saat, hizmet, tatil ve randevu ayarı `ManageBusiness`. "Personel sadece kendi randevularını görsün" ayarı açıksa Personel rolü `StaffAccess.ScopeAsync` ile bağlı personel kaydına süzülür. Randevular modülü kapalıysa randevu uçları 409 `appointments_module_disabled` (`AppointmentModuleFilter`).
- Pazarlama (Sprint 6A): `ViewMarketing` (Sahip, Yönetici, Mali Müşavir: kampanya sonuçları, kredi hareketleri), `ManageMarketing` (Sahip, Yönetici: kampanya oluştur/iptal), KVKK dışa aktarma `ManageContacts`, anonimleştirme `OwnerOnly`.
- Süper admin ayrı bir `PlatformAdmin` rolü. Sprint 6A'da policy `PlatformAdmin`: Identity rolü veya `Platform:AdminEmails` (user-secrets/ortam; e-postası doğrulanmış olmalı); her istekte okunur. Şimdilik sadece kredi yükleme (`/api/admin/businesses`, `/panel/platform/kredi`); tam panel Sprint 8. Kiracı filtresi rol bazında kapatılmaz; sadece `Features/Admin` endpoint'leri `IgnoreQueryFilters([QueryFilters.Tenant])` ile açıkça kapatır.
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

Sprint 2'de kuruldu (`Infrastructure/Jobs/BackgroundJobs.cs`): SQL Server depolama, aynı veritabanında `hangfire` şeması, 2 worker. Testlerde `Hangfire:Enabled=false`; iş sınıfları doğrudan çağrılarak test edilir. İşler kiracıdan bağımsız başlar, her işletme için ayrı DI scope'unda `TenantContext.Set` ile çalışır. Şu an: `reminders-due` (dakikada bir, vakti gelen hatırlatmaya bildirim), `import-jobs-cleanup` (saatlik), `due-digest` (her gün 09:00 Europe/Istanbul, Sprint 4: bugün vadesi gelen alacak varsa Sahip ve Yöneticilere gecikmiş toplamıyla birlikte bildirim; sadece gecikmiş varsa bildirim yok; `Businesses.LastDueDigestOn` koşullu UPDATE ile günde bir kez), `appointment-reminders` (dakikada bir, Sprint 5B: onaylı, hatırlatması gitmemiş, başlangıcı işletmenin hatırlatma saati içinde ve saati en az o kadar önce belirlenmiş randevu; satır başına koşullu UPDATE `ReminderSentAt` ile bir kez, sonra SMS; hata MessageLog'a Failed, yeniden denenmez; taşıma `ReminderSentAt`'i temizler), `phone-verifications-cleanup` (saatlik, 7 günden eski kodlar), `campaigns-dispatch` (dakikada bir + "hemen gönder"de kuyruğa; Sprint 6A: vadesi gelen kampanyayı koşullu UPDATE ile Sending yapar, alıcıları gönderim anında çözer, 100'lük parçalarla gönderir, 09:00-21:00 dışında bekler; testte `RunAsync(businessId)` ile işletmeye sınırlanır çünkü testler veritabanını paylaşır), `iys-sync` (5 dakikada bir; Pending izinleri `IIysClient` ile bildirir, marka kodu yoksa bekler), `automations` (Sprint 6B; `Marketing:AutomationsCron`, varsayılan 5 dakikada bir, E2E'de dakikalık: günlük otomasyonlar gönderim saati geçince günde bir kez — `LastRunOn` koşullu UPDATE — ve ziyaret sonrası teşekkür/anket; her gönderimden önce `AutomationSends` benzersiz satırı; pazarlama otomasyonları `MarketingSms` ile izin + çıkış eki + 09:00-21:00 + günlük 1; vade hatırlatması bilgilendirme; testte `RunAsync(businessId)`). Hangfire paneli açık değil (PlatformAdmin, Sprint 8).

- Zamanlanmış kampanya gönderimi, doğum günü ve geri kazanma otomasyonları (günlük).
- Vade hatırlatmaları (günlük).
- Abonelik yenileme hatırlatması (7 gün önce), deneme bitişi.
- Gece yedekleme (SQL Server `BACKUP DATABASE`, Express'te SQL Agent olmadığı için).
- Hangfire paneli sadece PlatformAdmin'e açık.

### Dış servisler (arayüz + sahte uygulama)

Her dış servis bir arayüzün arkasında: `ISmsSender`, `IIysClient` (Sprint 6A; sahte `FakeIysClient`, gerçek İYS bağlantısı pilot öncesi), `IEmailSender`, `IPaymentProvider`, `IPdfRenderer` (Sprint 2: QuestPDF Community lisansı, gömülü Lato fontu Türkçe karakterleri destekler). Excel okuma/yazma ClosedXML ile.
Geliştirmede sahte (fake) uygulamalar çalışır: SMS'ler veritabanındaki `MessageLog`'a ve konsola yazılır, e-postalar MailKit ile SMTP üzerinden Mailpit'e gider (`Email:Smtp`). Gerçek sağlayıcılar pilot öncesi bağlanır.
SMS (Sprint 5B, `Infrastructure/Messaging`): `ISmsSender` (`SmsSender`) önce `MessageLog` satırını yazar, sonra `ISmsGateway`'e (şimdilik `FakeSmsGateway`) gönderir; hata fırlatmaz, sonuç satırın Status/Error alanında. Segment hesabı `Domain/Messaging/SmsSegments` (GSM-7 160/153, Türkçe karakterde UCS-2 70/67), metinler `SmsTexts` (doğrulama kodu tek segment; uzun işletme adı kısaltılır). `Sms:MirrorToEmail` açıkken sahte sağlayıcı her SMS'i `sms+905…@sms.test` adresine Mailpit'e de yollar: E2E ve Development'ta açık (E2E kodu oradan okur, test için ayrı uç yok); Production'da açıksa uygulama başlamaz (options doğrulaması). Randevu SMS'leri `AppointmentNotifier` (onay, hatırlatma, taşıma, iptal, ret; iptal linki /r/[slug]/randevu/[token], token bir kez üretilir, özeti ve Data Protection ile şifreli hali randevuda). Testlerde `CapturingSmsGateway`.
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
- Panel düzeni: masaüstünde koyu sol menü; telefonda alt çubuk (Bugün, Randevu, +, Satış, Müşteri) ve üst çubukta sağda Menü (kullanıcı bölümü + ana menü; `components/panel/mobile-menu.tsx`).
- Takvim (Sprint 5A): kendi ızgaramız (`components/appointments/time-grid.tsx`; ücretli takvim kütüphanesi yok), sürükle-bırak `@dnd-kit/core` (MIT). Pencereler masaüstünde ortada, telefonda alttan (`ResponsivePanel`).
- Erişilebilirlik: klavye ile kullanılabilir, görünür odak, form etiketleri.

## Geliştirme ortamı

- Windows 11 ana makine, Mac'te de çalışmalı. Satır sonları LF (`.gitattributes`).
- `docker-compose.yml`:
  - `sqlserver`: `mcr.microsoft.com/mssql/server:2025-latest`, port 1433, `MSSQL_PID=Developer`, SA şifresi `.env`'den, kalıcı volume.
  - `mailpit`: `axllent/mailpit`, SMTP 1025, arayüz http://localhost:8025.
- Bağlantı dizesi ve gizli değerler: `.env` (Docker için) ve `dotnet user-secrets` (API için). `.env.example` git'te.

## Canlı ortam (pilot öncesi)

Tek VPS (4-8 GB RAM), Docker Compose: API, Next.js, SQL Server Express, Caddy (otomatik HTTPS). GitHub Actions ile build ve deploy. Cloudflare DNS. Ayrıntılar Sprint 8'de.

- Data Protection anahtarları (`DataProtection:KeysPath`) kalıcı volume'da tutulur ve sertifikayla şifrelenir; birden fazla API örneği aynı depoyu paylaşır. Kaybolursa oturumlar düşer ve tahsilat linkleri "Paylaş"ta yeniden üretilir (karar günlüğü 2026-10-11).
