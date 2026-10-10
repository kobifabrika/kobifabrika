# KobiFabrika — Windows'ta başlangıç ve Claude Code promptları

Bu dosya senin için. Sırayla uygula. Her promptu Claude Code'a olduğu gibi yapıştırabilirsin.

---

## 1. Programları kur (bir kez)

**PowerShell**'i aç (Başlat → "PowerShell"). Yönetici olarak açmana gerek yok. Komutları tek tek çalıştır:

```powershell
winget install --id Git.Git -e
winget install --id OpenJS.NodeJS.LTS -e
winget install --id Microsoft.DotNet.SDK.10 -e
winget install --id Docker.DockerDesktop -e
winget install --id Microsoft.VisualStudioCode -e
irm https://claude.ai/install.ps1 | iex
```

- Docker Desktop kurulunca bilgisayarı yeniden başlat. Docker, WSL 2 isterse çıkan uyarıyı kabul et (gerekirse PowerShell'de `wsl --install`). Docker Desktop'ı bir kez açıp "Engine running" yazısını gör.
- Hepsi bitince **yeni** bir PowerShell penceresi aç ve EF Core aracını kur:

```powershell
dotnet tool install --global dotnet-ef
```

Kontrol (her biri bir sürüm numarası yazmalı):

```powershell
git --version; node -v; dotnet --version; docker version; dotnet ef --version; claude --version
```

`dotnet --version` 10 ile başlamalı.

**VS Code eklentileri** (Eklentiler sekmesinde arat): C# Dev Kit, SQL Server (mssql), Tailwind CSS IntelliSense, ESLint, Claude Code.

İsteğe bağlı: API denemek için `winget install --id Bruno.Bruno -e`.

## 2. Proje klasörünü hazırla

1. `C:\Projeler\kobifabrika` klasörünü oluştur. **Masaüstü veya Belgeler'e koyma**: OneDrive bu klasörleri eşitler ve `node_modules` ile sorun çıkarır.
2. Bu zip'in içindekileri bu klasöre çıkar. Klasörde `CLAUDE.md`, `BASLA.md`, `docs`, `.claude` görünmeli (`.claude`, `.gitignore` ve `.gitattributes` gizli dosyalardır; Gezgin'de Görünüm → Göster → Gizli öğeler).
3. PowerShell'de:

```powershell
cd C:\Projeler\kobifabrika
git config --global user.name "Emir Kaya"
git config --global user.email "<ürün Gmail adresin>"
git config --global core.autocrlf false
git init -b main
git add .
git commit -m "Proje dokümanlarını ekle"
```

GitHub hesabı açılınca: GitHub'da boş, **private** bir `kobifabrika` reposu oluştur, sonra:

```powershell
git remote add origin https://github.com/<kullanici>/kobifabrika.git
git push -u origin main
```

## 3. Claude Code'u başlat

```powershell
cd C:\Projeler\kobifabrika
claude
```

İlk açılışta tarayıcıda giriş yap (Claude aboneliğin Pro veya üstü olmalı).

**İzinler:** Claude Code tam yetkiyle (bypassPermissions) çalışır, izin sormaz. Shift+Tab ile Plan moduna geçebilirsin.

**Faydalı komutlar:** `/clear` (yeni bir işe başlarken konuşmayı temizler; CLAUDE.md ve docs sayesinde kaldığı yeri bilir), `/compact` (uzun konuşmayı özetler), `Esc` (çalışan işi durdurur), `/status`.

---

## 4. Promptlar

### Prompt 1 — Tanışma ve Sprint 0 planı (Plan modunda)

`Shift+Tab` ile **Plan** moduna geç, sonra yapıştır:

```
CLAUDE.md dosyasını ve docs klasöründeki tüm dosyaları baştan sona oku. docs/tasarim-referans içindeki iki HTML dosyasına da bak.

Sonra:
1. Bu bilgisayarda git, Node.js, .NET 10 SDK, Docker ve dotnet-ef kurulu ve çalışıyor mu kontrol et. Eksik varsa kurma, bana söyle.
2. docs/05-sprintler.md'deki Sprint 0 için adım adım ayrıntılı bir uygulama planı çıkar: oluşturacağın klasörler ve projeler, kuracağın NuGet ve npm paketleri (sürümleriyle), docker-compose içeriği, kiracı (BusinessId) altyapısının nasıl çalışacağı ve testi, frontend tema token'larının nasıl tanımlanacağı.
3. Dokümanlarda çelişki ya da eksik karar görürsen listele ve sor.

Henüz kod yazma.
```

Planı oku. Değiştirmek istediğin bir şey varsa yaz. Uygunsa bir sonraki prompta geç.

### Prompt 2 — Sprint 0'ı uygula

`Shift+Tab` ile **Accept edits** moduna dön:

```
Planı onaylıyorum. Sprint 0'ı uygula.

Kurallar:
- Her büyük adımdan sonra derle ve çalıştır; hata varsa düzeltmeden devam etme.
- SQL Server şifresini .env'ye koy, .env.example'a örnek değer yaz. API bağlantı dizesi dotnet user-secrets'ta olsun.
- Kiracı izolasyonu için en az bir entegrasyon testi yaz ve geçtiğini göster.
- Frontend'de renkleri docs/04-tasarim.md'deki token adlarıyla tanımla. Ürün adı her yerde KobiFabrika olsun.
- Bitince şunların çalıştığını kendin doğrula: docker compose up -d; API /health 200 dönüyor; /scalar açılıyor; frontend localhost:3000'de açılıyor; panel iskeleti 375px genişlikte düzgün.
- README.md'ye Windows ve Mac için kurulum ve çalıştırma adımlarını yaz. CLAUDE.md'deki Komutlar bölümünü gerçek komutlarla güncelle.
- docs/05-sprintler.md'de Sprint 0 maddelerini işaretle, Durum satırını güncelle, docs/karar-gunlugu.md'ye aldığın teknik kararları ekle.
- Mantıklı aralıklarla Türkçe commit at.

Sonunda bana: neler yapıldı, hangi adresleri açıp kontrol etmeliyim, açık kalan bir şey var mı, kısaca yaz.
```

**Sen kontrol et:** tarayıcıda http://localhost:3000 (site ve panel iskeleti), http://localhost:5080/scalar (API dokümanı), http://localhost:8025 (Mailpit). Telefon görünümü için tarayıcıda F12 → cihaz simgesi → iPhone.

### Prompt 3 — Sprint 1 planı (Plan modunda)

```
/clear
```
sonra Plan modunda:

```
CLAUDE.md'yi ve docs/05-sprintler.md'yi oku; Sprint 0'ın bittiğini doğrula. Sprint 1 (Çekirdek) için ayrıntılı plan çıkar: ekranlar, endpoint'ler, tablolar ve migration, roller ve yetki policy'leri, e-posta akışları (Mailpit), testler. docs/01-faz1-kapsam.md'deki A bölümünü ve docs/04-tasarim.md'deki giriş ekranı kurallarını esas al. Kod yazma.
```

### Prompt 4 — Sprint 1'i uygula

```
Planı onaylıyorum. Sprint 1'i uygula. Sprint 0'daki kurallar geçerli: her adımda derle ve test et, kiracı izolasyon testlerini yaz, 375px'te kontrol et, sprint dosyasını ve karar günlüğünü güncelle, Türkçe commit at. Bitince neyi nasıl deneyeceğimi adım adım yaz (örnek: kayıt ol → Mailpit'te doğrulama e-postası → giriş → sihirbaz).
```

### Sonraki sprintler için şablon

Her sprintte aynı döngü: `/clear` → Plan modunda plan → onay → uygula → sen dene.

Plan:
```
CLAUDE.md'yi ve docs/05-sprintler.md'yi oku. Sprint <N> için ayrıntılı plan çıkar; docs/01-faz1-kapsam.md'deki ilgili bölümü ve docs/03-veri-modeli.md'yi esas al. Önceki sprintlerde kurulan yapıya uy. Kod yazma.
```

Uygula:
```
Planı onaylıyorum. Sprint <N>'i uygula. Her adımda derle ve test et, kiracı izolasyon testlerini yaz, 375px'te kontrol et, sprint dosyasını ve karar günlüğünü güncelle, Türkçe commit at. Bitince neyi nasıl deneyeceğimi adım adım yaz.
```

### Hata alırsan

```
Şu hatayı alıyorum: <hatayı veya ekran görüntüsünü yapıştır>. Nedenini bul, düzelt, tekrar çalıştırıp düzeldiğini göster.
```

### Tasarımı beğenmezsen

```
<ekran adı> ekranını değiştir: <ne istediğini yaz>. docs/04-tasarim.md'deki renk ve kontrast kurallarına uy, 375px ve masaüstünde kontrol et.
```

---

## 5. Bu sohbetle bağlantı

Her sprint bitince Claude Code'un yazdığı özeti bu sohbete (KobiFabrika projesi) yapıştır. Notion'daki plan ve Yapılacaklar sayfalarını ben güncellerim, bir sonraki sprintin promptunu gerekirse düzenlerim.
