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

## İlk otomatik dağıtım (yarın ilk iş)

- `DEPLOY_SSH_KEY` secret'ı eklendi. #58 yeşil ama dağıtım "atlandı" (secret dağıtım işi başladığında henüz yoktu).
- #59: testler yeşil, dağıtım imajları derledi ama **aktarımda durdu** (`tar: bin: Cannot stat`): `.gitignore`'daki .NET `bin/`
  kuralı `deploy/canli/bin/`'i dışarıda bırakmıştı. Sunucuya dokunulmadı, canlı eski (bu PC'den 2026-10-23'te yapılan) dağıtımla
  çalışıyor. Düzeltme `.gitignore` istisnası + betikler (sunucudakilerle sağlama toplamı aynı).
- **#60 (`40018440519`, ilk gerçek otomatik dağıtım): sonucu henüz bilinmiyor.** Kontrol: `gh run view 40018440519`; kırmızıysa
  `gh run view 40018440519 --log-failed`. Yeşilse: `ssh kf 'cd /opt/kobifabrika && docker compose ps && docker image ls | grep kobifabrika && ls yedek'`
  (yeni etiketli imajlar `current`, `onceki` var, `...-dagitim.bak.zst` yedeği alınmış), sonra
  https://kobifabrika.com, https://app.kobifabrika.com, https://api.kobifabrika.com/health → 200; Sentry/BOM düzeltmesinin canlıda
  olduğunu doğrula (`ProductionHosting.cs`: bozuk DSN'de API düşmez; denemek için sunucuda `.env`'deki `SENTRY_DSN`'e elle geçici
  bozuk değer yazıp `docker compose up -d api` → kayıtta "UYARI: Sentry:Dsn geçersiz" ve api healthy; sonra değeri boşa geri al).
  Sonucu karar günlüğüne (2026-10-23 8B kaydı) ve `docs/05-sprintler.md`'deki GitHub Actions maddesine işle.

## Sonraki adımlar

1. **Cloudflare turuncu bulut:** sunucu tarafı hazır (Caddy `trusted_proxies` + `CF-Connecting-IP`, API ForwardedHeaders).
   SSL/TLS → **Full (strict)**, dört kaydı turuncuya al; `curl -sI https://kobifabrika.com` (cf-ray başlığı), sertifika ve
   gerçek IP kontrolü (ör. geçici olarak Caddy erişim kaydında `{client_ip}` veya API'de rate limit davranışı).
2. **E-posta:** Brevo hesabı → SMTP kullanıcı/anahtar `.\deploy\gizli-ekle.ps1 BREVO_SMTP_USER` ve `BREVO_SMTP_KEY`;
   Cloudflare'de SPF/DKIM/DMARC + Email Routing (destek@, bildirim@). Kendi hesabınla kayıt olup e-postanı doğrula
   (PlatformAdmin için doğrulanmış e-posta şart), `/platform` ve `https://api.kobifabrika.com/hangfire`'ı dene.
   Hazır olunca `ssh kf sudo /opt/kobifabrika/bin/yayin-modu.sh tam`.
3. **İzleme/yedek anahtarları:** Sentry DSN (`gizli-ekle.ps1 SENTRY_DSN`), R2 (`R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`,
   `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`; kovaya 30 gün yaşam döngüsü), sonra `ssh kf sudo /opt/kobifabrika/bin/yedek-al.sh elle` ile
   R2 gönderimini dene. UptimeRobot: `https://api.kobifabrika.com/health` ve `https://kobifabrika.com`.
   `YEDEK_SIFRE`'yi parola yöneticisine al (sunucuda: `sudo grep YEDEK_SIFRE /opt/kobifabrika/.env`, ekranda kimse yokken).
4. Açık kalanlar: Cloudflare Web Analytics token'ı
   (`NEXT_PUBLIC_CF_ANALYTICS_TOKEN`, workflow'daki web derlemesine build-arg); Search Console kapsam kontrolü (site tekrar yayında);
   Güzel Hosting kredisi/destek talebi #IXA-330501 kapandı mı.
