# Canlı ortam

Tek KVM sunucu, Docker Compose: SQL Server Express, API, Next.js (web), Caddy (HTTPS). Kurulum 2026-10-23 (Sprint 8B).
Dosyaların kaynağı repoda `deploy/canli/`; sunucuda `/opt/kobifabrika`. Gizli değerler sadece sunucudaki `.env`'de.

| | |
|---|---|
| Sunucu | Güzel Hosting Private Cloud 2 · KVM · 4 çekirdek, 4 GB RAM (+4 GB swap dosyası, 1 GB swap bölümü; swappiness 10), 60 GB SSD · Ubuntu 24.04 · 70.40.139.67 · `kf-app.kobifabrika.com` |
| Bağlantı | `ssh kf` → `deploy` kullanıcısı (sudo, şifresiz; şifreli giriş ve root girişi kapalı, sadece anahtar) |
| Alan adları | `kobifabrika.com` site + herkese açık sayfalar (`/r/[slug]`, `/a`, `/c`, `/ekstre`, `/odeme`, `/teklif`) · `www` → kök (301) · `app.` panel (kök `/panel`'e gider; kök alandaki `/panel` ve `/platform` → `app.`) · `api.` API |
| .com.tr | `kobifabrika.com.tr` ve `www.kobifabrika.com.tr` → `https://kobifabrika.com` (301, yol ve sorgu korunur) Cloudflare'de kurulu (2026-10-23, http/https denendi); sunucuya gelmez |
| DNS | Cloudflare, dört A kaydı → 70.40.139.67, şimdilik **DNS only** (gri bulut); turuncuya geçiş için bkz. [Cloudflare](#cloudflare-turuncu-bulut) |
| Sertifika | Let's Encrypt, Caddy alır ve yeniler (`caddy_data` biriminde) |
| Saat | Sunucu ve konteynerler UTC; uygulama İstanbul saatini kendisi hesaplar |
| Yayın modu | **site** (sadece tanıtım sitesi, `NEXT_PUBLIC_SITE_ONLY=1`; kayıt/giriş "Pilot listesine katıl", uygulama yolları "Çok yakında"). API ve veritabanı tam çalışır. Bkz. [Yayın modu](#yayın-modu-site--tam) |

## Konteynerler

| Servis | İmaj | Bellek sınırı | Ölçülen (boşta, 2026-10-23) | Not |
|---|---|---|---|---|
| `sqlserver` | `mcr.microsoft.com/mssql/server:2025-latest` (2025 CU10, **Express**) | 2 GB (`MSSQL_MEMORY_LIMIT_MB=1600`) | ~520 MB | Port dışarı açık değil; veri `mssql_data` biriminde |
| `api` | `kobifabrika-api:current` | 700 MB | ~130 MB | İç port 8080; `api_data` (Data Protection anahtarları, logo dosyaları), `api_logs` |
| `web` | `kobifabrika-web-<site\|tam>:current` | 450 MB | ~105 MB | Next.js standalone, iç port 3000 |
| `caddy` | `caddy:2.10.2-alpine` | 100 MB | ~20 MB | Dışarıya açık tek servis: 80, 443 (TCP + UDP) |

Toplam kullanım ~1,35 GB; ~2,5 GB boş. Hepsi `restart: unless-stopped`, Docker açılışta etkin: yeniden başlatmada
~60 sn'de site ve API kendiliğinden geri gelir (2026-10-23'te denendi). İç ağ sabit `172.30.0.0/24`.

API'nin canlı ayarları `deploy/canli/compose.yml`'de (ortam değişkeni): cookie alanı `.kobifabrika.com` (Secure, HttpOnly,
SameSite=Lax), CORS sadece `https://app.kobifabrika.com` ve `https://kobifabrika.com`, SMS kapalı (`Messaging:SmsEnabled=false`,
`Sms:MirrorToEmail=false`, Mailpit yok), e-posta Brevo SMTP (`smtp-relay.brevo.com:587`; anahtar gelene kadar gönderim
başarısız olur ve kuyrukta Failed'a düşer, uygulama etkilenmez), PlatformAdmin `emirkayaco@gmail.com` (e-postası doğrulanmış
olmalı), Hangfire paneli `https://api.kobifabrika.com/hangfire` sadece PlatformAdmin'e, Scalar ve `/openapi` canlıda yok
(404). Veritabanına API `kf_app` girişiyle (db_owner) bağlanır; `sa` sadece migration, yedek ve bakım için.

## Bağlanma, durum, kayıtlar

```bash
ssh kf                                              # deploy@70.40.139.67
cd /opt/kobifabrika
docker compose ps                                   # dört servis; api/web/sqlserver "healthy"
docker stats --no-stream                            # bellek
docker compose logs --tail 100 api                  # api | web | caddy | sqlserver; -f ile canlı izle
docker compose exec api ls /app/logs                # API dosya kayıtları (14 gün)
curl -s https://api.kobifabrika.com/health          # Healthy (veritabanı dahil)
systemctl list-timers 'kobifabrika-*'               # sıradaki gece yedeği, Cloudflare listesi yenilemesi
docker compose logs --since 10m caddy | grep http.log.access   # erişim kaydı (client_ip = gerçek istemci IP'si)
sudo fail2ban-client status sshd                    # engellenen IP'ler
```

SQL'e bakmak (şifre ekrana gelmez): `bash -c '. bin/ortak.sh; sql "SELECT name FROM sys.databases"'`.

Yeniden başlatma: tek servis `docker compose restart api`; hepsi `docker compose up -d`; sunucu `sudo systemctl reboot`.

## Dağıtım

**Otomatik:** `main`'e push → GitHub Actions "Testler" işi yeşilse → "Dağıtım" işi (`.github/workflows/test.yml`):

1. API imajı ve iki web imajı (site, tam) Actions'ta derlenir (`backend/Dockerfile`, `frontend/Dockerfile`; buildx cache).
2. `deploy/canli/` (compose, Caddyfile, `bin/`, `systemd/`) ve imajlar SSH ile aktarılır: `docker save | zstd | ssh → docker load`
   (registry yok). Sunucu anahtarı `deploy/canli/known_hosts`'ta.
3. Sunucuda `bin/dagit.sh <etiket>`: **yedek** (`yedek-al.sh dagitim`) → **migration** (API imajındaki EF bundle `/app/efbundle`,
   `sa` ile) → `kf_app` girişi (`sql-kullanici.sh`) → eski imajlar `:onceki`, yeniler `:current` → `docker compose up -d` →
   konteyner sağlığı + `https://api.kobifabrika.com/health`. Geçmezse **önceki imajlara döner** ve iş kırmızı olur.
   Her imajın son 2 sürümü (`current`, `onceki`) kalır.

Dağıtım işi iptal edilmez ve sırayla çalışır (yeni push sadece test koşusunu iptal eder). Sadece doküman değişen commit'te
iş akışı hiç çalışmaz.

Actions'ın anahtarı sadece `deploy` kullanıcısının `authorized_keys`'inde `command="/opt/kobifabrika/bin/actions-girisi.sh",restrict`
ile durur: kabuk, pty ve port yönlendirme yok; sadece `yukle`, `dosyalar`, `dagit <etiket>`.

**Secret (bir kez):** GitHub → kobifabrika/kobifabrika → Settings → Secrets and variables → Actions → New repository secret →
Name `DEPLOY_SSH_KEY`, Secret: bu bilgisayardaki `~/.ssh/kf_dagitim` dosyasının tamamı (panoya kopyalamak, ekranda göstermeden:
`Get-Content $HOME\.ssh\kf_dagitim -Raw | Set-Clipboard`). Secret yoksa dağıtım işi "atlandı" yazar ve yeşil biter.
Anahtar kaybolursa/sızarsa: yenisini üret (`ssh-keygen -t ed25519 -N "" -C kobifabrika-actions-dagitim -f ~/.ssh/kf_dagitim`),
sunucuda `~deploy/.ssh/authorized_keys`'teki `kobifabrika-actions-dagitim` satırını yenisiyle değiştir, secret'ı güncelle.

**Elle (bu bilgisayardan; Git Bash):**

```bash
E=$(date -u +%Y%m%d-%H%M)-elle
docker build --platform linux/amd64 -t kobifabrika-api:$E backend
docker build --platform linux/amd64 --build-arg NEXT_PUBLIC_SITE_ONLY=1 -t kobifabrika-web-site:$E frontend
docker build --platform linux/amd64 --build-arg NEXT_PUBLIC_SITE_ONLY=0 -t kobifabrika-web-tam:$E frontend
K="ssh -i ~/.ssh/kf_dagitim -o IdentitiesOnly=yes -o UserKnownHostsFile=deploy/canli/known_hosts deploy@70.40.139.67"
tar -czf - -C deploy/canli compose.yml Caddyfile bin systemd | $K dosyalar
docker save kobifabrika-api:$E kobifabrika-web-site:$E kobifabrika-web-tam:$E | gzip -1 | $K yukle   # ~3 dk
$K dagit $E
```

(PowerShell 5.1 ikili veriyi boruda bozar; Git Bash kullan.)

## Geri dönüş

- **Kod (imaj):** `ssh kf`, sonra
  `cd /opt/kobifabrika && for i in kobifabrika-api kobifabrika-web-site kobifabrika-web-tam; do docker tag $i:onceki $i:current; done && docker compose up -d`.
  Ya da eski commit'i `git revert` edip push (Actions yeniden dağıtır).
- **Migration geri alınmaz.** Yeni migration veriyi bozduysa yedekten dönülür (aşağıda); her dağıtım migration'dan hemen önce
  `yedek/KobiFabrika-<zaman>-dagitim.bak.zst` alır.

## Yedek

- `kobifabrika-yedek.timer`: her gece **03:30 İstanbul** (`OnCalendar=... Europe/Istanbul`, kaçırılırsa açılışta) →
  `bin/yedek-al.sh gece`. Ayrıca her dağıtımda migration öncesi (`dagitim`). Elle: `sudo /opt/kobifabrika/bin/yedek-al.sh elle`.
- `BACKUP DATABASE ... WITH CHECKSUM` → `/opt/kobifabrika/yedek/KobiFabrika-<UTC>-<etiket>.bak.zst`. **Express sürümü
  `WITH COMPRESSION` desteklemiyor** ("not supported on Express Edition"); yedek zstd ile sıkıştırılır (boş veritabanı ~370 KB).
- 7 günden eski yedekler silinir.
- **R2 (pasif):** `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET` dolunca her yedek `YEDEK_SIFRE` ile
  şifrelenip (AES-256, `openssl enc -pbkdf2`) `r2:<kova>/veritabani/`'na da gider (`bin/r2-gonder.sh`, `rclone/rclone:1.75.1`).
  R2'de eski kopyaları silmek için kovaya yaşam döngüsü kuralı (ör. 30 gün) eklenir. Henüz anahtarlarla denenmedi.
  `YEDEK_SIFRE` olmadan R2'deki kopyalar açılamaz: değeri ayrıca güvenli bir yerde (parola yöneticisi) sakla.

### Yedekten dönme

```bash
ssh kf
ls -lt /opt/kobifabrika/yedek | head                                          # yedekler
sudo /opt/kobifabrika/bin/yedek-geri-yukle.sh deneme                          # en yeni yedeği ayrı adla açar, karşılaştırır, siler
sudo /opt/kobifabrika/bin/yedek-geri-yukle.sh canli yedek/KobiFabrika-....bak.zst
```

`canli`: API'yi durdurur, **önce şimdiki hali yedekler** (`geri-yukleme-oncesi`), KobiFabrika'yı yedekle değiştirir, `kf_app`'i
yeniden bağlar, API'yi açar. Geri yükleme denemesi 2026-10-23'te yapıldı (79 tablo, 19 migration; canlıyla aynı).

R2'den dönüş: kopyayı indir, `openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass env:YEDEK_SIFRE -in X.enc -out X.bak.zst`,
`/opt/kobifabrika/yedek/`'e koy, yukarıdaki komut.

## Gizli değerler

Sunucuda `/opt/kobifabrika/.env` (`deploy:deploy`, `600`). İlk kurulumda `bin/ilk-kurulum.sh` rastgele üretir (openssl), ekrana
basmaz: `MSSQL_SA_PASSWORD`, `APP_DB_PASSWORD`, `DP_CERT_PASSWORD`, `YEDEK_SIFRE`. Boş başlayanlar: `BREVO_SMTP_USER`,
`BREVO_SMTP_KEY`, `SENTRY_DSN`, `R2_*`. Boşken uygulama hatasız açılır (Sentry kapalı, e-posta gönderilemez, R2 pasif).
Değerler `.env`'de tek tırnak içinde durur (compose ve bash için birebir).

**Ekleme/değiştirme (bu bilgisayardan):** `.\deploy\gizli-ekle.ps1` (ya da `.\deploy\gizli-ekle.ps1 BREVO_SMTP_KEY`). Değer
gösterilmeden sorulur, ssh'nin standart girdisiyle sunucudaki `bin/gizli-yaz.sh`'ye gider (komut satırına/kayda girmez),
`.env`'ye yazılır, ilgili servis yeniden kurulur ve sağlığı beklenir (açılmazsa uyarır). Baştaki BOM ve boşluklar atılır;
`SENTRY_DSN` biçimi denetlenir. Sunucuda: `sudo /opt/kobifabrika/bin/gizli-yaz.sh AD`, değeri yapıştır, Enter.

Bu yolla değişmeyenler:

- `MSSQL_SA_PASSWORD`: önce SQL'de `ALTER LOGIN sa WITH PASSWORD = '<yeni>'` (sqlcmd), sonra `.env`'yi elle düzenle,
  `docker compose up -d sqlserver`.
- `DP_CERT_PASSWORD` / `sertifika/dp.pfx`: Data Protection anahtarları bu sertifikayla şifreli. Sertifika ya da şifre
  kaybolursa anahtarlar açılmaz: kullanıcılar yeniden giriş yapar, şifre sıfırlama/doğrulama linkleri geçersiz olur,
  tahsilat linkleri çalışır ama "Paylaş" yeni link üretir (karar günlüğü 2026-10-11). Taşırken `.env` ile birlikte taşı.

## Yayın modu (site / tam)

Her dağıtım iki web imajı getirir; geçiş derleme istemez, tek komut:

```bash
ssh kf sudo /opt/kobifabrika/bin/yayin-modu.sh tam    # site + panel: kayıt, giriş, panel, online randevu açılır
ssh kf sudo /opt/kobifabrika/bin/yayin-modu.sh site   # geri: sadece tanıtım sitesi
```

`.env`'deki `WEB_VARIANT`'ı değiştirir ve sadece `web`'i yeniden kurar (birkaç saniye). **`tam`'a geçmeden önce** Brevo SMTP
anahtarı girilmiş ve e-posta DNS'i (SPF/DKIM/DMARC) hazır olmalı: kayıt doğrulaması e-postayla.

## Sıfırdan kurulum / başka sunucuya taşıma

Yeni sunucuda (Ubuntu 24.04, KVM; OpenVZ'de SQL Server çalışmaz — `systemd-detect-virt` "kvm" demeli):

1. **Güvenlik** (root olarak, sağlayıcının verdiği anahtar/şifreyle): `apt update && apt full-upgrade`; `deploy` kullanıcısı
   (`adduser --disabled-password deploy`, `usermod -aG sudo deploy`, `/etc/sudoers.d/90-deploy`: `deploy ALL=(ALL) NOPASSWD:ALL`),
   root'un `authorized_keys`'ini `/home/deploy/.ssh/`'ye kopyala. **`ssh deploy@<ip>` çalıştığını gördükten sonra**
   `/etc/ssh/sshd_config.d/00-kobifabrika.conf`: `PermitRootLogin no`, `PasswordAuthentication no`,
   `KbdInteractiveAuthentication no`, `AllowUsers deploy` (ilk okunan değer geçerli; `50-cloud-init.conf` şifreyi açıyordu,
   onu da `no` yap), `sshd -t && systemctl reload ssh`, `sshd -T | grep -i -E 'permitroot|password'` ile doğrula.
   ufw (`deny incoming`; 22/tcp herkese; 80/443 sadece Cloudflare'e, adım 3'te `bin/cloudflare-ip.sh` yazar), fail2ban (`/etc/fail2ban/jail.d/kobifabrika.local`: sshd,
   `/var/log/auth.log`, 10 dk'da 5 hata → 1 saat, `banaction = ufw`), unattended-upgrades (`20auto-upgrades`), 4 GB
   `/swapfile` + `vm.swappiness=10` (`/etc/sysctl.d/90-kobifabrika.conf`), `timedatectl set-timezone UTC`.
2. **Docker** resmi depodan (`docker-ce`, `docker-compose-plugin`), `/etc/docker/daemon.json`: `local` kayıt sürücüsü (10 MB × 3),
   `live-restore`; `usermod -aG docker deploy`. Docker'ın yayınladığı portlar ufw'den önce işlenir: compose'ta 80/443 dışında
   port yayınlama.
3. **Dosyalar:** `sudo install -d -o deploy -g deploy /opt/kobifabrika/{bin,systemd}`; `deploy/canli/`'yı kopyala
   (`scp compose.yml Caddyfile kf:/opt/kobifabrika/`, `scp bin/*.sh kf:/opt/kobifabrika/bin/`, `scp systemd/* kf:/opt/kobifabrika/systemd/`),
   `chmod 755 bin/*.sh`, `sudo bin/ilk-kurulum.sh` (.env, Data Protection sertifikası, yedek zamanlayıcısı).
   **Taşımada** yeni `.env` üretme: eski sunucunun `.env`'sini ve `sertifika/dp.pfx`'ini aynen taşı (şifreler ve anahtarlar aynı kalsın).
4. `docker compose up -d sqlserver` → `docker compose logs sqlserver | grep "ready for client connections"`.
5. Actions anahtarı: `~deploy/.ssh/authorized_keys`'e `command="/opt/kobifabrika/bin/actions-girisi.sh",restrict <kf_dagitim.pub>`;
   `ssh-keyscan -t ed25519 <ip> > deploy/canli/known_hosts` (parmak izini sunucudaki `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`
   ile karşılaştır); workflow'daki `HEDEF`'i ve bu dosyadaki IP'yi güncelle.
6. **Veri taşıma:** eski sunucuda `sudo bin/yedek-al.sh tasima`, dosyayı yeni sunucunun `yedek/`'ine kopyala, ilk dağıtımdan
   (migration) sonra `sudo bin/yedek-geri-yukle.sh canli yedek/<dosya>`. Logo dosyaları `api_data` biriminde:
   `docker run --rm -v kobifabrika_api_data:/v -v $PWD:/y alpine tar czf /y/api_data.tgz -C /v .` ile taşınır.
7. İlk dağıtım (Actions veya elle), sonra Cloudflare'de A kayıtlarını yeni IP'ye çevir (turuncu bulut kalır); sertifikaları
   Caddy kendisi alır. `ilk-kurulum.sh` 80/443'ü sadece Cloudflare'e açar: yeni sunucuyu doğrudan IP'den deneyemezsin
   (gerekirse geçici olarak `sudo bin/cloudflare-ip.sh --kaldir`, sonra `sudo bin/cloudflare-ip.sh`).
8. Doğrula: `https://kobifabrika.com`, `/fiyatlar`, `/sitemap.xml`, `https://app.kobifabrika.com`, `https://api.kobifabrika.com/health`
   → 200, `www` → 301, bir kez `sudo systemctl reboot` ve her şeyin kendiliğinden geldiğini gör.

## Cloudflare (turuncu bulut)

2026-10-24'ten beri `@`, `www`, `app`, `api` **Proxied**; SSL/TLS **Full (strict)** (Flexible yönlendirme döngüsü yapar),
Always Use HTTPS açık, en düşük TLS 1.2. Yanıtlarda `server: cloudflare` ve `cf-ray` görünür.

**Gerçek istemci IP'si:** Caddy Cloudflare'in IP aralıklarına güvenir (`Caddyfile` başı, `trusted_proxies`) ve oradan gelen
istekte gerçek IP'yi `CF-Connecting-IP`'den alır; doğrudan gelen istekte bu başlık yok sayılır. API'ye ve web'e
`X-Forwarded-For` olarak tek değer gider; API sadece Docker ağından (`172.30.0.0/24`) gelen bu başlığa güvenir
(`ForwardedHeaders:KnownNetworks`): rate limit gerçek IP'yle çalışır. Kontrol: Caddy erişim kaydında `remote_ip` Cloudflare,
`client_ip` ziyaretçi; API istek kaydı `... responded 200 in 2.1 ms from <ip>`:

```bash
docker compose logs --since 10m caddy | grep http.log.access | tail -3
docker compose logs --since 10m api | grep responded | tail -3
```

**Güvenlik duvarı:** 80/443 sadece Cloudflare aralıklarına açık; 22 herkese. `bin/cloudflare-ip.sh` listeyi resmi kaynaktan
(https://www.cloudflare.com/ips-v4, /ips-v6) indirir, biçimini denetler (indirilemez/bozuksa son kayıtlı liste,
`/opt/kobifabrika/cloudflare-ip/`) ve iki yere yazar:

- `DOCKER-USER` → `KF-CLOUDFLARE` zinciri (asıl kısıt): Docker'ın yayınladığı portlar DNAT ile FORWARD'dan geçer, **ufw'ye
  uğramaz**. eth0'dan 80/tcp, 443/tcp, 443/udp'ye gelen yeni bağlantı Cloudflare aralığında değilse düşer.
- ufw: aynı aralıklar `# cloudflare` yorumlu kurallar (INPUT / docker-proxy yolu); herkese açık 80/443 kuralları silinir.

`kobifabrika-cloudflare-ip.service` açılışta (Docker'dan sonra), `.timer` her pazartesi 04:00 çalıştırır. Elle:

```bash
sudo bin/cloudflare-ip.sh            # indir + uygula (değişiklik yoksa ufw'ye dokunmaz)
sudo bin/cloudflare-ip.sh --yerel    # indirmeden kayıtlı listeyi uygula
sudo bin/cloudflare-ip.sh --kaldir   # kısıtı kaldır, 80/443 herkese (turuncu buluttan çıkarken / sunucuyu IP'den denerken)
sudo iptables -L KF-CLOUDFLARE -v -n # düşen paket sayacı son satırda
sudo journalctl -u kobifabrika-cloudflare-ip -n 20
```

Liste değişince `Caddyfile`'daki `trusted_proxies` de aynı listeye çekilmeli (elle; betik uyarmaz, haftalık kayda bakılır:
`ips-v4 güncellendi`). Doğrudan IP'den deneme (`curl --resolve kobifabrika.com:443:70.40.139.67 https://kobifabrika.com`)
zaman aşımına düşmeli.

**Sertifika yenilemesi:** Let's Encrypt doğrulaması Cloudflare'den geçer. TLS-ALPN-01 Cloudflare arkasında çalışmaz (TLS'i
Cloudflare sonlandırır); HTTP-01 çalışır: Always Use HTTPS `/.well-known/acme-challenge/` isteğini 308 ile HTTPS'e
yönlendirir, Let's Encrypt yönlendirmeyi izler ve Caddy jetonu 443'te de verir. 2026-10-24'te denendi: Caddy deposuna sahte
bir jeton yazıldı, `http://kobifabrika.com/.well-known/acme-challenge/<jeton>` Cloudflare üzerinden doğru yanıtı döndü, jeton
silindi. Sertifikalar Ocak 2027'ye kadar geçerli; Caddy ~30 gün kala yeniler (kayıtta `certificate obtained successfully`).

## Sorun giderme

- **Site açılmıyor, Cloudflare 521/522:** güvenlik duvarı Cloudflare'i engelliyor olabilir (aralık listesi değişmiş):
  `sudo bin/cloudflare-ip.sh`, olmazsa `sudo bin/cloudflare-ip.sh --kaldir`. Turuncu bulut kapatılırsa da `--kaldir` şart.
- **R2'ye gönderilemedi (403)**: `R2_BUCKET` kova adı mı (küçük harf, tire; anahtar adı değil), token'ın o kovada "Object
  Read & Write" yetkisi var mı, `R2_ACCOUNT_ID` hesap kimliği mi. Düzeltip `sudo bin/yedek-al.sh elle`. Dağıtımı durdurmaz.
- **Caddyfile değişti ama etkisi yok:** dosya tek dosya olarak bağlı; yeni inode'la yazılırsa (`install`, `scp` sonrası `mv`)
  konteyner eskisini görür. Yerinde yaz (`cat yeni > Caddyfile`) ya da `docker compose up -d --force-recreate --no-deps caddy`.
  `dagit.sh` bu durumu algılayıp Caddy'yi yeniden oluşturur.

- **Sertifika alınamıyor**, Caddy kaydında `lookup ... connection refused`: `docker compose up -d --force-recreate caddy`.
- **502**: web/api yeni başlıyor ya da düştü: `docker compose ps` (healthy olmalı), `docker compose logs --tail 100 api`.
- **API sürekli yeniden başlıyor** (`Restarting`): `docker compose logs --tail 50 api`; çoğunlukla bozuk bir `.env` değeri.
  `gizli-yaz.sh` ile doğru değeri (ya da boş) yaz. (2026-10-23: BOM'lu Sentry DSN API'yi düşürmüştü; artık geçersiz DSN'de Sentry
  kapalı kalır, uyarı yazılır.)
- **SQL Server açılmıyor:** `docker compose logs sqlserver`; bellek (`free -m`), disk (`df -h`). 2025 imajı açılmazsa
  `.env`'ye `MSSQL_IMAGE_TAG=2022-latest` (aynı veri birimi 2025'ten 2022'ye geri açılmaz; o durumda boş birim + yedekten dönüş).
- **Disk doldu:** `docker system df`, `docker image prune -a` (sadece `current`/`onceki` kullanılıyor), `du -sh /opt/kobifabrika/yedek`.

## Benden istenecekler / açık kalanlar

- GitHub secret `DEPLOY_SSH_KEY` (yukarıda; `gh` hesabının bu depoda yönetici yetkisi yok).
- Brevo hesabı + SMTP anahtarı, e-posta DNS (SPF/DKIM/DMARC), Cloudflare Email Routing → sonra `yayin-modu.sh tam`.
- Sentry DSN, R2 anahtarları + kova (+ yaşam döngüsü kuralı), UptimeRobot (`https://api.kobifabrika.com/health`).
