# Canlı ortam

Asıl canlı ortam (API, panel, SQL Server, yedek, izleme) Sprint 8B'de ayrı bir KVM sunucuya kurulacak; bu dosya o kurulumla
tamamlanacak. Şimdilik sadece geçici site sunucusu var.

## Geçici site sunucusu (2026-10-21, yaklaşık 1 ay)

Tanıtım sitesi (kobifabrika.com) panel ve API hazır olana kadar burada yayında. Frontend **sadece-site modunda** derlenir
(`NEXT_PUBLIC_SITE_ONLY=1`, `frontend/src/content/site-mode.ts`): kayıt/giriş çağrıları "Pilot listesine katıl" olur ve
hazır mesajlı WhatsApp linkine gider; `/giris`, `/kayit`, `/panel`, `/r/...` gibi API isteyen yollar "Çok yakında" gösterir
(noindex); tarayıcı API'ye hiç istek atmaz.

| | |
|---|---|
| Sunucu | Güzel Hosting TR-VPS-4 · 70.40.139.194 · Ubuntu 22.04 · OpenVZ 7 · 4 GB RAM · ana makine adı `kf-pilot.kobifabrika.com` |
| Bağlantı | `ssh kf` (root, sadece anahtarla; şifreyle giriş kapalı) |
| Klasör | `/opt/kobifabrika-site` (`compose.yml`, `Caddyfile`; kaynakları repoda `deploy/site/`) |
| Konteynerler | `web` (Next.js standalone, imaj `kobifabrika-site:current`, 512 MB sınır) · `caddy` (`caddy:2.10.2-alpine`, 128 MB sınır) |
| Alan adları | `kobifabrika.com` site · `www` → kök (301) · `app.` ve `api.` → "Çok yakında" (noindex, robots her şeyi kapatır) |
| DNS | Cloudflare, dört A kaydı → 70.40.139.194, **DNS only** (gri bulut) |
| Sertifika | Let's Encrypt, Caddy alır ve yeniler (`caddy_data` biriminde) |
| Açılış | Docker açılışta etkin, konteynerler `restart: unless-stopped`; yeniden başlatmada ~5 sn'de site geri gelir (denendi) |

Ölçülen (2026-10-21): web ~80 MB, caddy ~26 MB; sunucuda toplam ~220 MB kullanılıyor. Lighthouse mobil: ana sayfa performans 91,
erişilebilirlik/en iyi uygulamalar/SEO 100; /fiyatlar 96/100/100/100.

### Sunucuda yapılanlar

- Hosting şablonundaki test imajları (mssql, hello-world) silindi. SQL Server bu sunucuda çalışmıyor (OpenVZ'de çöküyor).
- SSH: `/etc/ssh/sshd_config.d/00-kobifabrika.conf` → `PermitRootLogin prohibit-password`, `PasswordAuthentication no`
  (eski ayar yedeği `/root/sshd_config.yedek`).
- ufw: gelen trafik kapalı, açık olanlar 22/tcp, 80/tcp, 443/tcp, 443/udp (HTTP/3). Docker'ın yayınladığı portlar ufw'den önce
  işlenir; compose'ta sadece 80/443 yayınlanır, başka port yayınlama.
- fail2ban: `/etc/fail2ban/jail.d/kobifabrika.local` (sshd, 10 dakikada 5 hata → 1 saat ufw ile engel). Ubuntu'da SSH birimi
  `ssh.service` olduğu için journal yerine `/var/log/auth.log` okunur (journal eşleşmesi `sshd.service` arıyordu).
- unattended-upgrades: günlük güvenlik güncellemesi (`/etc/apt/apt.conf.d/20auto-upgrades`).
- Hosting şablonundaki Apache (varsayılan sayfa) 80'i tutuyordu: `systemctl disable --now apache2` (paket duruyor).
- OpenVZ'de ağı ifupdown (`venet0`) yönetiyor; `systemd-networkd-wait-online` her açılışta 2 dakika bekleyip Docker'ı
  geciktiriyordu: `systemctl mask systemd-networkd-wait-online`.
- OpenVZ'de bellek sınırları (cgroup v1) uygulanıyor: `docker stats` 512 MiB / 128 MiB gösterir.

### Güncelleme (site değişince)

İmaj bu bilgisayarda üretilir, sunucuda derleme yapılmaz. Git Bash'te (PowerShell 5.1 ikili veriyi boruda bozar):

```bash
cd frontend
TAG=$(date +%Y%m%d-%H%M)
docker build --platform linux/amd64 -t kobifabrika-site:$TAG .     # NEXT_PUBLIC_SITE_ONLY=1 Dockerfile'da varsayılan
docker save kobifabrika-site:$TAG | gzip -1 | ssh kf 'gunzip | docker load'
ssh kf "docker tag kobifabrika-site:current kobifabrika-site:onceki; docker tag kobifabrika-site:$TAG kobifabrika-site:current \
  && cd /opt/kobifabrika-site && docker compose up -d && docker image prune -f"
curl -sI https://kobifabrika.com/ | head -1                         # HTTP/2 200
```

Geri alma: `ssh kf "docker tag kobifabrika-site:onceki kobifabrika-site:current && cd /opt/kobifabrika-site && docker compose up -d"`.

Caddyfile veya compose değişince:

```bash
scp deploy/site/Caddyfile deploy/site/compose.yml kf:/opt/kobifabrika-site/
ssh kf 'cd /opt/kobifabrika-site && docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile'   # sadece Caddyfile
ssh kf 'cd /opt/kobifabrika-site && docker compose up -d'                                                       # compose
```

Durum ve kayıtlar: `ssh kf 'cd /opt/kobifabrika-site && docker compose ps && docker stats --no-stream && docker compose logs --tail 50'`.

### Kapatma (asıl sunucuya geçince)

1. Yeni sunucuda site ve panel yayında olsun; Cloudflare'de dört A kaydını yeni IP'ye çevir (TTL kısa: Auto).
2. Yeni sunucuda sertifikalar alınıp `https://kobifabrika.com` doğrulandıktan sonra:
   `ssh kf 'cd /opt/kobifabrika-site && docker compose down'`
3. Güzel Hosting panelinden VPS'i iptal et. Sunucuda saklanacak veri yok (site imajı ve dosyalar repodan yeniden üretilir;
   sertifikayı yeni sunucunun Caddy'si kendisi alır).

### Sorun giderme

- **Sertifika alınamıyor**, Caddy kaydında `lookup ... on 127.0.0.53 ... connection refused`: konteyner Docker'ın iç DNS'i
  olmadan oluşmuş (ör. port çakışmasıyla yarım kalan ilk başlatma). `docker compose up -d --force-recreate caddy`.
- **80/443 dolu** (`address already in use`): `ss -ltnp | grep -E ':(80|443) '`; hosting şablonundan gelen bir web sunucusu
  açılmış olabilir (Apache kapatıldı).
- **502**: web konteyneri yeni başlıyor ya da düştü; `docker compose ps` (healthy olmalı) ve `docker compose logs web`.
