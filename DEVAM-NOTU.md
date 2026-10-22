# Devam notu

## 2026-10-23: durum

- **Sunucu:** Güzel Hosting **TR-VPS-4 (OpenVZ) silindi**, ücret iadesi hesaba **kredi** olarak geldi. Bununla birlikte
  `ssh kf` ve geçici sunucudaki tanıtım sitesi de gitti; kobifabrika.com şu an bu sunucudan yayında değil.
  Sıradaki sunucu: **Private Cloud 2 (KVM, Ubuntu 24.04)**. Sipariş, kredinin nasıl kullanılacağı netleşince verilecek.
  Geçici sunucunun kurulum notları (Caddy, sadece-site modu, güvenlik adımları) `docs/canli-ortam.md`'de; yeni sunucuda tekrar kullanılır.
- **GitHub Actions #54** (`c2a76c4`: tutar biçimi, İlk kurulum makalesi, lint): **sonucu henüz bilinmiyor** (izleme kapatılırken koşuyordu).
  Yerelde yeşil: backend (tam koşudaki tek hata düzeltildi, ilgili sınıflar 38/38), Vitest 325, lint; değişen 17 E2E dosyası
  telefon-chromium + SMS + sadece-site (25 senaryo) ve fiyat/yardım senaryoları masaüstünde.
- **Actions #53** (`cd8ed1a`) yeşil; karar günlüğüne ve `docs/05-sprintler.md` Durum satırına işlendi.
- **2026-10-22 yapılanlar:** tutarlar her yerde tek kuralla ("1.299 TL", "1.250,50 TL"; `formatMoney` / `Money.Format`),
  fiyattaki "1 .299" boşluğu giderildi, "İlk kurulum" makalesi düğme adını arayüzden alıyor. Ayrıntı: karar günlüğü 2026-10-22.
- **Search Console:** doğrulanmıştı, sitemap gönderilmişti (site kapalıyken tarama hata verebilir).

## İlk yapılacaklar

1. **Actions #54 sonucuna bak** (`gh run list -L 3`). Kırmızıysa `gh run view <id> --log-failed` ve Playwright raporu; düzelt,
   yeşil olana kadar takip et; yeşilse karar günlüğüne (2026-10-22 kaydı) ve `docs/05-sprintler.md` Durum satırına işle.
2. Güzel Hosting: kredi kullanımı netleşince Private Cloud 2 (KVM, Ubuntu 24.04) siparişi; karar ve fiyatı `docs/05-sprintler.md` 8B'ye ve
   karar günlüğüne yaz. Destek talebi #IXA-330501.
3. Yeni sunucuda önce tanıtım sitesini güncel imajla geri aç (`docs/canli-ortam.md` adımları; Ubuntu 24.04 farklarına dikkat),
   sonra 8B'nin asıl kısmı: API + panel + SQL Server, e-posta DNS, yedek, izleme. `docs/canli-ortam.md`'yi yeni sunucuya göre güncelle.
4. **Next.js 16.4.2 yükseltmesi (ayrı iş, bekliyor):** 16.4.0 için güvenlik uyarısı var. Ajanla yapılan her `next build` bir kez
   duruyor ("SecurityFatalError", tekrar denenince geçiyor; CI etkilenmiyor). `npm i next@16.4.2` + lint/Vitest/build + Actions tam koşu.
5. Açık kalanlar:
   - kobifabrika.com.tr → kobifabrika.com yönlendirmesi (DNS kaydı + Caddy bloğu).
   - Cloudflare Web Analytics token'ı (`NEXT_PUBLIC_CF_ANALYTICS_TOKEN`, imaj yeniden derlenir).
   - Search Console: site yeniden açılınca kapsam/dizine ekleme durumunu kontrol et.
