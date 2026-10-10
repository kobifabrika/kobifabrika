# Karar günlüğü

En yeni en üstte. Notion'daki "Proje Planı ve Kararlar" sayfasıyla aynı kararlar; teknik kararlar burada daha ayrıntılı.

- **2026-10-10 (Sprint 0, ürün/tasarım):**
  - Panel yolları `/panel/...` altında; tanıtım sitesi `/`. Canlıda `app.kobifabrika.com` isteklerini `proxy.ts` `/panel/*`'a yönlendirecek (Sprint 8, deploy ile).
  - Frontend URL'leri Türkçe (`/giris`, `/kayit`, `/panel/musteriler`); API yolları İngilizce.
  - Giriş sadece e-posta + şifre; kullanıcı adı yok.
  - Durum renkleri: başarı `#15803d`, uyarı `#b45309`, hata `#b91c1c` (açık zeminleri `#ecfdf3`, `#fffbeb`, `#fef2f2`).
  - Odak halkası: açık zeminde `brand-500`, koyu zeminde `brand-100` (`#00fdff` beyazda 1.3:1, görünmez).
  - Menü grupları: İşletme (Bugün, Randevular, Satışlar, Müşteriler, Ürünler/Hizmetler, Stok, Cari ve Kasa, Kampanyalar), Yönetim (Personel, Mesai ve İzin, Raporlar, Ayarlar). Taslaktaki "Belgeler" yok.
  - Panel düzeni: lg (1024px) ve üstünde koyu sol menü; altında üst çubuk + alt sekme çubuğu (Bugün, Randevu, +, Satış, Müşteri, Menü).
- **2026-10-10 (Sprint 0, teknik):**
  - Solution `.slnx` (.NET 10 varsayılanı); NuGet sürümleri `Directory.Packages.props`'ta merkezi; `TreatWarningsAsErrors` açık. `global.json` SDK 10.0.100+ (latestFeature).
  - Test: xUnit v3 (`xunit.v3.mtp-v2`) + Microsoft Testing Platform (`global.json` → `test.runner`), Shouldly, Testcontainers.MsSql. FluentAssertions ticari lisanslı olduğu için kullanılmıyor. Testler kendi SQL Server 2025 konteynerini açar; Docker gerekir.
  - Id'ler `Guid.CreateVersion7()` (sıralı, istemcide). Identity anahtarları da Guid; `CreatedBy` = kullanıcı Id'si.
  - Kiracı altyapısı: `ITenantScoped` / `ISoftDeletable` / `IAuditable`, taban sınıf `TenantEntity`. EF Core 10 adlı query filter'lar: `Tenant` ve `SoftDelete` ayrı; çöp kutusu gibi ekranlar sadece `SoftDelete`'i kapatabilir. `BusinessId` null ise sorgu boş döner (fail-closed). Kurallar `AppDbContext.SaveChanges` override'ında: yeni kayda BusinessId atanır; başka işletmenin Id'si, başka işletmenin kaydını değiştirme/silme ve BusinessId değiştirme `TenantViolationException` (API'de 403). `ExecuteUpdate/Delete` SaveChanges'e uğramaz ama filtre onlara da uygulanır (test edildi).
  - `TenantContext` scoped ve bir scope içinde bir kez set edilir; panelde claim'den (`kf:business_id`), public endpoint ve Hangfire işlerinde açıkça.
  - PlatformAdmin için kiracı filtresi global olarak kapatılmaz; sadece `Features/Admin` endpoint'leri `IgnoreQueryFilters([QueryFilters.Tenant])` ile açıkça kapatır.
  - `BusinessMembership` kiracı filtresi dışında (`[TenantExempt]`): girişte kiracı bilinmeden okunur, her zaman UserId ile süzülür. Silinmiş işletmenin üyelikleri `SoftDelete` filtresiyle gizlenir.
  - Konvansiyonlar: `decimal` → `decimal(18,2)`, `DateTime` → `datetime2` + UTC dönüştürücü (yerel saat yazılırsa hata), `string` varsayılan `nvarchar(256)`; daha uzun alanlar açıkça ayarlanır. `Business.Settings` SQL Server 2025 native `json` tipinde (compatibility level 170).
  - `Business.PlanId` Plans tablosu gelince (Sprint 8) eklenecek. Telefon için Identity'nin `PhoneNumber` alanı kullanılıyor (ayrı `Phone` yok).
  - Hangfire, QuestPDF, FluentValidation ilk kullanıldıkları sprintte eklenecek (1, 2, 5).
  - API: Serilog (konsol + `logs/api-.log`), `X-Correlation-Id`, Türkçe başlıklı ProblemDetails, OpenAPI `/openapi/v1.json` + Scalar `/scalar` (sadece Development), `/health` (DB kontrolü), `/api/system/info`. Identity cookie `kf.auth` (httpOnly, SameSite=Lax), API yönlendirme yapmaz, 401/403 döner.
  - Frontend: Next.js 16.4 varsayılanları korundu (Turbopack, `cacheComponents` + `partialPrefetching`, Tailwind 4 `@tailwindcss/turbopack`). TypeScript 5.x (create-next-app'in kurduğu). Geist fontu kaldırıldı, sistem fontu.
  - Tema: token'lar `globals.css` `@theme` içinde, Tailwind'in hazır paleti kapalı (`--color-*: initial`); shadcn semantik değişkenleri marka token'larına bağlı. Kontrast kuralları Vitest ile `globals.css`'ten okunarak test ediliyor.
  - shadcn/ui: radix tabanı, `radix-nova` stili. Dokunma alanı için buton/input yükseklikleri 44px'e çıkarıldı, input yazısı her ekranda 16px, odak halkası tam opak.
  - Vitest 5'te `@vitejs/plugin-react` kullanılmıyor (Babel 8 peer çakışması); Vite 8 JSX'i ve tsconfig yollarını kendisi çözüyor.
  - API tipleri (`src/lib/api/schema.d.ts`) commit'lenir; backend değişince `npm run gen:api`.
  - GitHub remote bağlandı (github.com/kobifabrika/kobifabrika); "GitHub açılana kadar yerel git" kararı geçersiz.

- **2026-10-10:** Claude Code tam yetkiyle (bypassPermissions) çalışacak; git push serbest. Sprint sonlarında ve büyük adımlardan sonra commit + push.
- **2026-10-10:** Kodlamaya geçildi. Ana geliştirme makinesi Windows PC (Mac'te de devam edilebilir). Frontend Next.js olarak kesinleşti; admin.html ve index.html sadece tasarım referansı. Kod dili İngilizce, arayüz Türkçe. Tek veritabanı + BusinessId ile çok kiracılık. Cookie tabanlı ASP.NET Core Identity. GitHub hesabı açılana kadar yerel git.
- **2026-10-10:** Faz 1 kapsamı, Faz 2 eklemeleri, paket fiyat aralığı (Kalfa 599-799, Usta 1.199-1.499 TL/ay) ve 9 maddelik konumlanma onaylandı. Sprint planı 9 sprint, yaklaşık 4 ay.
- **2026-10-09:** Ürün adı KobiFabrika, şirket KobiFabrika Yazılım A.Ş.
- **2026-10-07:** Renk paleti: #000a1d, #00385a, #006ea6, #00b0ff, #00fdff. Personel, mesai ve izin Faz 1'de.
- **2026-10-06:** Veritabanı MSSQL (T-SQL). Mobil uygulama yok, responsive web.
