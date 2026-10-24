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
- **Gerçek IP:** Caddy erişim kaydında `client_ip=31.223.77.108`, `remote_ip` Cloudflare; API istek kaydında `from 31.223.77.108`.
- **Güvenlik duvarı:** 80/443 sadece Cloudflare aralıklarına (`bin/cloudflare-ip.sh`; `DOCKER-USER` → `KF-CLOUDFLARE` + ufw
  `# cloudflare`; açılışta ve pazartesi 04:00). Doğrudan IP'den 80/443 zaman aşımı, SSH 22 açık. Sunucuya elle kuruldu; bu
  dağıtımla repodaki sürüm aynısı gelir.
- **Let's Encrypt:** HTTP-01 Cloudflare arkasında çalışıyor (sahte jetonla denendi). Sertifikalar Ocak 2027'ye kadar geçerli; Aralık
  sonunda `docker compose logs caddy | grep -i "certificate obtained"` ile yenilendiğine bak.
- **Düzeltilen hata:** dağıtım Caddyfile'ı yeni inode'la yazıyordu, `caddy reload` eskisini okuyordu (artık yerinde yazılıyor).
- **#61 dağıtımı durdu:** `.env`'de `R2_BUCKET` yanlıştı → R2 403 → yedek betiği hata verdi. R2 hatası artık dağıtımı durdurmaz
  (`e275e35`). Kova adı düzeltildi; #62'de dağıtım yeşil, şifreli kopya `kobifabrika-yedek/veritabani/`'ye gitti. API kaydında
  `from 31.223.77.108` doğrulandı.

## 2026-10-24: e-posta, Sentry, tam moda hazırlık

- **E-posta:** Brevo (ücretsiz) authenticated, DKIM + DMARC; Gmail'de SPF/DKIM/DMARC PASS, doğrudan ve destek@ yönlendirmesi Gelen
  Kutusu'nda. 587 STARTTLS zorunlu, sistem e-postalarında Reply-To destek@, günlük sınır 280 (dolunca kuyruk).
- **Sentry:** API ve web (sunucu tarafı) açık, kişisel veri maskeli, performans izleme kapalı. Deneme olayları: API
  `6577b3d18b014d518736e92769577ee3`, API BOM'lu DSN `c33dcfaebd484d17bbb96e0fe840bd33`, web `b9842e27d9d54494ac19f7fdf1bb50c3`
  (Sentry'de maskeleme kontrolü proje sohbetinde, Chrome'dan). Bozuk DSN → api healthy + "UYARI" denendi.
- **Tam mod:** HENÜZ açılmadı (site-only sürüyor). Kontrol listesi `docs/canli-ortam.md` → Tam moda geçiş kontrol listesi.
  Emir'in onayıyla: `ssh kf sudo /opt/kobifabrika/bin/yayin-modu.sh tam`, sonra listedeki 8 adım (ilk kayıt emirkayaco@gmail.com).


## Sonraki adımlar

1. **Tam moda geçiş** (Emir onaylayınca): `ssh kf sudo /opt/kobifabrika/bin/yayin-modu.sh tam`, sonra `docs/canli-ortam.md` →
   Tam moda geçiş kontrol listesi (kayıt → doğrulama e-postası → kurulum → giriş → şifre sıfırlama → `/platform` → `/hangfire`).
   Tutmazsa `yayin-modu.sh site`.
2. **UptimeRobot** (ücretsiz): `https://api.kobifabrika.com/health` ve `https://kobifabrika.com`, 5 dk.
3. `YEDEK_SIFRE`'yi parola yöneticisine al (sunucuda: `sudo grep YEDEK_SIFRE /opt/kobifabrika/.env`, ekranda kimse yokken);
   R2 kovasına 30 gün yaşam döngüsü kuralı var mı bak.
4. Açık kalanlar: Cloudflare Web Analytics token'ı (`NEXT_PUBLIC_CF_ANALYTICS_TOKEN`, workflow'daki web derlemesine build-arg);
   Search Console kapsam kontrolü; Güzel Hosting destek talebi #IXA-330501 kapandı mı.
