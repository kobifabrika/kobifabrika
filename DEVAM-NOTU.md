# Devam notu

## 2026-10-21 gecesi: durum

- **Sunucu:** Güzel Hosting TR-VPS-4 (70.40.139.194, Ubuntu 22.04, **OpenVZ 7**, 4 GB), `ssh kf` (root, sadece anahtar).
  **SQL Server bu sunucuda çalışmıyor** (OpenVZ'de çöküyor); panel ve API için ayrı KVM sunucu gerekecek.
- **Yayında:** tanıtım sitesi **sadece-site modunda** (`NEXT_PUBLIC_SITE_ONLY=1`): https://kobifabrika.com, `www` → kök 301,
  `app.` ve `api.` → "Çok yakında" (noindex). Caddy + Let's Encrypt, `/opt/kobifabrika-site`. Yeniden başlatmada kendiliğinden geliyor (denendi).
  Ayrıntı, güncelleme, geri alma, kapatma: `docs/canli-ortam.md` → Geçici site sunucusu.
- **Sunucu güvenliği:** şifreyle SSH kapalı, ufw (22/80/443), fail2ban, unattended-upgrades; şablondaki Apache kapatıldı.
- **Ölçüm:** web ~80 MB, caddy ~26 MB; Lighthouse mobil ana sayfa 91/100/100/100, /fiyatlar 96/100/100/100.
- **Search Console:** doğrulandı, sitemap gönderildi.
- **Güzel Hosting destek talebi #IXA-330501:** satış biriminden cevap bekleniyor.
- **GitHub Actions #53** (`cd8ed1a`, sadece-site modu + belgeler): **yeşil** (2026-10-22 kontrol; backend 656, Vitest 313, E2E 152).
  #52 yeni push yüzünden iptal oldu. Yerelde yeşil: Vitest 313, `@sadece-site` E2E (masaüstü + telefon-chromium), değişen site/hesap/online randevu E2E'leri.

## Yarın ilk yapılacaklar

1. ~~Actions #53 sonucuna bak~~ (2026-10-22: yeşil; karar günlüğüne ve `docs/05-sprintler.md` Durum satırına işlendi).
2. Siteyi kontrol et: `curl -sI https://kobifabrika.com/ | head -1`, `ssh kf 'cd /opt/kobifabrika-site && docker compose ps'`.
3. Güzel Hosting #IXA-330501 cevabına bak: KVM sunucu (SQL Server için) seçeneği/fiyatı; karar ver, `docs/05-sprintler.md` 8B'ye yaz.
4. Search Console: kapsam/dizine ekleme durumunu kontrol et (site haritası okundu mu).
5. Açık kalanlar:
   - kobifabrika.com.tr → kobifabrika.com yönlendirmesi (DNS kaydı + Caddy bloğu).
   - Cloudflare Web Analytics token'ı (`NEXT_PUBLIC_CF_ANALYTICS_TOKEN`, imaj yeniden derlenir).
   - Yardım makalesi "İlk kurulum" hâlâ "Ücretsiz dene"ye dokun diyor (sadece-site modunda düğme "Pilot listesine katıl").
   - Fiyatlar masaüstünde "1.299 TL" rakamları arasında boşluk görünüyor (tabular rakam; yeni değil, incelenmeli).
6. Sonra 8B'nin asıl kısmı: KVM sunucuda API + panel + SQL Server, e-posta DNS, yedek, izleme.
