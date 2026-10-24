# Devam notu

## 2026-10-23: canlı sunucu kuruldu (Sprint 8B)

- **Sunucu:** Güzel Hosting Private Cloud 2, KVM, 4 çekirdek / 4 GB / 60 GB, Ubuntu 24.04, 70.40.139.67. `ssh kf` → `deploy`
  (root ve şifreli giriş kapalı). Her şey `docs/canli-ortam.md`'de (bağlanma, kayıtlar, dağıtım, geri dönüş, yedek, gizli değerler,
  yayın modu, taşıma, Cloudflare).
- **Yayında:** https://kobifabrika.com (site, **sadece-site modu**), https://app.kobifabrika.com ("Çok yakında"),
  https://api.kobifabrika.com/health (API + SQL Server 2025 Express tam çalışıyor, 19 migration, canlı veritabanı boş başladı).
  Sertifikalar Let's Encrypt (Ocak 2027'ye kadar, Caddy yeniler). Reboot denendi: ~60 sn'de kendiliğinden geliyor.
- **Bellek (boşta):** SQL Server ~520 MB / 2 GB, API ~130 / 700 MB, web ~105 / 450 MB, Caddy ~20 / 100 MB; makinede ~2,5 GB boş.
- **Yedek:** gece 03:30 İstanbul + her dağıtımda migration öncesi, 7 gün, `/opt/kobifabrika/yedek`; geri yükleme denendi. R2 pasif.
- **Next.js 16.4.2** yükseltildi (`a347d78`).

## 2026-10-24: Cloudflare turuncu bulut

- **Otomatik dağıtım çalışıyor:** #60 (`67a28dd`) test + dağıtım yeşil, dağıtım öncesi yedek alındı, `current`/`onceki` imajları var.
- **Cloudflare:** dört kayıt Proxied, Full (strict), Always Use HTTPS, TLS ≥ 1.2. Üç adres `cf-ray` ile 200 (app kökü → `/panel` 302).
- **Gerçek IP:** Caddy erişim kaydında `client_ip=31.223.77.108`, `remote_ip` Cloudflare. API istek kaydına `from <ip>` eklendi
  (bu commit'le dağıtılıyor). **Kontrol et:** `ssh kf 'cd /opt/kobifabrika; docker compose logs --since 30m api | grep responded | tail'`
  → Cloudflare IP'si (104.x, 172.64-71.x, 162.158.x …) değil, ziyaretçi IP'si görünmeli.
- **Güvenlik duvarı:** 80/443 sadece Cloudflare aralıklarına (`bin/cloudflare-ip.sh`; `DOCKER-USER` → `KF-CLOUDFLARE` + ufw
  `# cloudflare`; açılışta ve pazartesi 04:00). Doğrudan IP'den 80/443 zaman aşımı, SSH 22 açık. Sunucuya elle kuruldu; bu
  dağıtımla repodaki sürüm aynısı gelir.
- **Let's Encrypt:** HTTP-01 Cloudflare arkasında çalışıyor (sahte jetonla denendi). Sertifikalar Ocak 2027'ye kadar geçerli; Aralık
  sonunda `docker compose logs caddy | grep -i "certificate obtained"` ile yenilendiğine bak.
- **Düzeltilen hata:** dağıtım Caddyfile'ı yeni inode'la yazıyordu, `caddy reload` eskisini okuyordu (artık yerinde yazılıyor).
- **#61 dağıtımı durdu:** `.env`'de `R2_BUCKET` değeri kova adı değil, düz `R2_BUCKET` metni → R2 403 → yedek betiği hata verdi.
  Düzeltme: R2 hatası artık dağıtımı durdurmaz (uyarı). **Gerçek kova adını yaz:** `.\deploy\gizli-ekle.ps1 R2_BUCKET`, sonra
  `ssh kf sudo /opt/kobifabrika/bin/yedek-al.sh elle` → "R2'ye gönderildi". Düzelene kadar gece yedeği yerelde alınır ama
  zamanlayıcı "failed" görünür.
- Hâlâ bekleyen: Sentry/BOM düzeltmesinin canlı denemesi (bozuk `SENTRY_DSN` → api healthy, kayıtta "UYARI: Sentry:Dsn geçersiz").

## Sonraki adımlar

1. **E-posta:** Brevo hesabı → SMTP kullanıcı/anahtar `.\deploy\gizli-ekle.ps1 BREVO_SMTP_USER` ve `BREVO_SMTP_KEY`;
   Cloudflare'de SPF/DKIM/DMARC + Email Routing (destek@, bildirim@). Kendi hesabınla kayıt olup e-postanı doğrula
   (PlatformAdmin için doğrulanmış e-posta şart), `/platform` ve `https://api.kobifabrika.com/hangfire`'ı dene.
   Hazır olunca `ssh kf sudo /opt/kobifabrika/bin/yayin-modu.sh tam`.
2. **İzleme/yedek anahtarları:** Sentry DSN (`gizli-ekle.ps1 SENTRY_DSN`), R2 (`R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`,
   `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`; kovaya 30 gün yaşam döngüsü), sonra `ssh kf sudo /opt/kobifabrika/bin/yedek-al.sh elle` ile
   R2 gönderimini dene. UptimeRobot: `https://api.kobifabrika.com/health` ve `https://kobifabrika.com`.
   `YEDEK_SIFRE`'yi parola yöneticisine al (sunucuda: `sudo grep YEDEK_SIFRE /opt/kobifabrika/.env`, ekranda kimse yokken).
3. Açık kalanlar: Cloudflare Web Analytics token'ı
   (`NEXT_PUBLIC_CF_ANALYTICS_TOKEN`, workflow'daki web derlemesine build-arg); Search Console kapsam kontrolü (site tekrar yayında);
   Güzel Hosting kredisi/destek talebi #IXA-330501 kapandı mı.
