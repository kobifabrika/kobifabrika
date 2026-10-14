# Tasarım

Referans taslaklar: `docs/tasarim-referans/index.html` (giriş ekranı) ve `admin.html` (panel).
Bu dosyalar sadece görsel referanstır; içlerindeki "KOBIOS" adı eski çalışma adıdır, her yerde **KobiFabrika** yazılacak.
Logo henüz yok: logo gelene kadar "KobiFabrika" yazısı (wordmark) kullanılır.

## Renkler (site geneli, kesin — 2026-10-10)

Sitenin, giriş ekranlarının ve panelin tamamında **üç renk** var:

| Token | Hex | Kullanım |
|---|---|---|
| `black` | `#000000` | Yazı, ana buton (panel ve giriş), sol menü, üst çubuk, giriş ekranı marka paneli, sitede en fazla 2 siyah bölüm |
| `white` | `#ffffff` | Zemin, siyah zeminde yazı |
| `brand` | `#00b0ff` | Vurgu mavisi (wordmark'taki "Fabrika"): sitenin ana çağrı butonu, aktif menü, odak halkası, alt çizgi, küçük ikon |

- **Griler** (`neutral-50` … `neutral-900`) siyah-beyaz arası, renk tonu yok (R = G = B). Ayırıcı çizgi `neutral-200`, ikincil metin `neutral-600`, input kenarlığı `neutral-400`, siyah zeminde ikincil metin `neutral-300`.
- **Kırmızı** (`danger-600` #b91c1c, açık zemini `danger-50`) sadece hata mesajı ve silme gibi işlevsel uyarılarda.
- **Başarı için yeşil yok:** siyah (veya mavi ikon) + ✓. Uyarı için sarı yok: gri kutu + solda ince mavi çizgi.
- Kaldırılanlar (kodda, token'da, e-posta şablonunda kullanılmaz): `#000a1d`, `#00385a`, `#006ea6`, `#00fdff` ve onlardan türetilen tonlar; `success-*`, `warning-*`.
- Token'ların kodu: `frontend/src/app/globals.css` (Tailwind: `bg-black`, `bg-brand`, `border-neutral-200`, `text-danger-600`…). Tailwind'in hazır paleti kapalı.

### Kontrast kuralları

- Beyaz zeminde yazı/link: siyah (21:1), `neutral-700`, `neutral-600` (7.8:1). Link = siyah yazı + alt çizgi (alt çizgi mavi olabilir).
- **Mavi beyaz zeminde yazı olarak kullanılmaz** (2.1:1). Sadece çizgi, ikon, alt çizgi, vurgu zemini (ör. aktif menü, ilerleme çubuğu).
- Siyah zeminde mavi yazı/link olabilir (9.9:1).
- Mavi buton → yazı **siyah** (9.9:1). Siyah buton → yazı beyaz.
- Odak: mavi halka (3px). Beyaz zeminde mavi tek başına 3:1 vermediği için inputların kenarı odakta siyaha döner; butonlarda halka siyah butona bitişik durur.
- `danger-600` siyah zeminde yazı olamaz; siyah zeminde hata mesajı açık kutuda (`bg-danger-50 text-danger-600`).
- `src/lib/theme/contrast.test.ts` bu kuralları gerçek token değerleri üzerinden doğrular; eski renkler `globals.css`'e geri girerse test kırılır.

### Kapsamlar

- **Beyaz (varsayılan, `:root`):** tanıtım sitesi, giriş/kayıt/şifre/davet/e-posta doğrulama, kurulum sihirbazı, panel içerik alanı. Ana buton siyah (`primary`), sitenin çağrı butonu mavi (`<Button variant="accent">`).
- **Siyah (`.dark` sınıfı):** panelin üst çubuğu, sitedeki siyah bölümler. İçindeki semantik sınıflar (`bg-card`, `text-foreground`, `text-muted-foreground`, `text-primary`) kendiliğinden siyah zemine döner; burada `primary` mavidir.
- **Sol menü:** siyah zemin, `neutral-300` yazı, aktif öğe mavi zemin + siyah yazı, hover `neutral-800`.
- Giriş ekranları: solda siyah marka paneli (wordmark, slogan, şirket unvanı), sağda beyaz form; 768px altında marka paneli üstte ince bant.

## Tasarım ilkeleri (site ve panel)

Rakip incelemesi: `docs/rakip-site-notlari.md`.

- Ağırlıklı beyaz zemin, siyah yazı, bol boşluk, güçlü tipografi hiyerarşisi, **sola hizalı** düzen.
- **Mavi az:** her ekran alanında bir mavi öğe yeter (sitede bölüm başına: ana buton *ya da* bir kelimenin alt çizgisi *ya da* paketlerde önerilen paketin üst çizgisi).
- Bölümleri ayırmak için **gölge yerine ince çizgi** (`border-black` güçlü ayrım, `border-neutral-200` hafif). Kartlarda gölge yok.
- **Ürünü göster:** sitedeki ekranlar resim değil, panelin gerçek bileşenleriyle (`Card`, `Badge`, `Button`, menü) çizilir (`src/components/site/product-shots.tsx`), tarayıcı ve telefon çerçevesinde. Örnek veriler gerçekçi ve esnafa ait ("Ayşe Yılmaz — Saç kesimi, 14:30", "Bakiye 1.250 TL"); her çerçevenin altında "Örnek ekran" yazar.
- **Metin esnafın dilinde,** somut ve kısa: "Veresiye defterini kapat", "Gelmeyen müşteriyi geri çağır". Kalıp sözler yok: "devrim", "akıllı", "yapay zekâ destekli", "işinizi bir üst seviyeye taşıyın".
- **Yasak:** gradyan, neon ışıma, cam/blur kart, yüzen/eğik 3D kart, emoji, her bölümde ikon yağmuru, her şeyin ortalanması, her kartın aynı yuvarlak köşe + gölge alması, sayaç animasyonu, uydurma müşteri yorumu/logo/kullanıcı sayısı.
- İkonlar tek renk (siyah; siyah zeminde beyaz/gri) ve az; liste maddelerinde ✓ yeterli.
- **Animasyon en az:** sadece sitenin ilk bölümünde hafif belirme (`.kf-fade`), `prefers-reduced-motion: no-preference` iken. Kaydırmaya bağlı animasyon yok.
- Köşe yuvarlaklığı 6px (`--radius`); büyük yüzeylerde (çerçeve, tablo) köşe yok ya da az.
- Paket fiyatları tek dosyada: `frontend/src/content/plans.ts` (`monthlyPrice: null` → "Lansman fiyatı yakında").

## Tipografi

- **Başlık:** Schibsted Grotesk (`next/font/google`, kendi sunucumuzdan; `latin` + `latin-ext`). `h1`–`h3` ve `font-heading` sınıfı. Kalın (700), sıkı satır aralığı, hafif negatif harf aralığı.
- **Gövde:** sistem fontu yığını `-apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif` (Türkçe karakter sorunu yok, hızlı).
- Ölçek: 12 / 14 / 16 / 18 / 20 / 24 / 30 px panelde; sitede başlıklar 36–84px. Rakamlar `tabular-nums`. 12px'in altı yok.

## Taslaklardan alınan dil

- Minimal, sakin, beyaz zemin, ince kenarlıklar, 6px köşe yuvarlaklığı, gölge yok.
- Giriş ekranı: solda siyah marka paneli (KobiFabrika, slogan, şirket unvanı), sağda beyaz form (kullanıcı adı/e-posta, şifre + Göster, Beni hatırla, Şifremi unuttum, Giriş Yap, Üye Ol).
- Panel: siyah sol menü (YÖNETİM ve İŞLETME grupları, sayı rozetleri), üstte selamlama ("Günaydın, Emir."), 4 istatistik kartı, satış grafiği, personel durumu, son satışlar tablosu.
- Menü (Faz 1): **İşletme:** Bugün · Randevular · Satışlar · Müşteriler · Ürünler/Hizmetler · Stok · Cari ve Kasa · Kampanyalar. **Yönetim:** Personel · Mesai ve İzin · Raporlar · Ayarlar.

## Taslaklardaki hatalar (tekrarlanmayacak)

1. Giriş ekranı telefonda da 50/50 yan yana kalıyor → 768px altında marka paneli üstte ince bant, form tam genişlik.
2. 6-11px yazılar → en küçük 12px, input yazısı 16px (iPhone 16px altındaki inputlarda sayfayı yakınlaştırır).
3. `overflow: hidden` ile kaydırma kapatılmış → klavye açılınca form kesilmesin, kaydırma açık.
4. Unicode menü ikonları → lucide-react ikonları.

## Mobil

- 375px genişlikte her ekran kullanılabilir olmalı.
- Telefonda alt çubuk simetrik 5 düğme: Bugün, Randevu, ortada hızlı işlem (+), Satış, Müşteri (Randevular kapalıysa Randevu yerine Ürün). Menü sağ üstte, kullanıcı menüsüyle birleşik tek düğme (Sprint 6A).
- Yatay kaydırılan çip ve sekme satırları tek bileşen (`components/ui/scroll-row.tsx`: `ScrollRow`, `Chip`, `TabNav`): kaydırma çubuğu gizli, son öğenin sağında iç boşluk; kenarda solma (gradyan) yok.
- Dokunma alanı en az 44×44px. Tablolar telefonda kart listesine dönüşür.
