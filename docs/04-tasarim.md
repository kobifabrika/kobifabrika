# Tasarım

Referans taslaklar: `docs/tasarim-referans/index.html` (giriş ekranı) ve `admin.html` (panel).
Bu dosyalar sadece görsel referanstır; içlerindeki "KOBIOS" adı eski çalışma adıdır, her yerde **KobiFabrika** yazılacak.
**Logo:** `frontend/public/marka/logo.png` (siyah kare zeminde beyaz "KOBI" + mavi "FABRIKA"). Sitede, giriş/kayıt ekranında, panelin koyu menüsünde ve sistem e-postalarında kırpılmış hali kullanılır (`components/brand/logo.tsx`, `next/image`); **sadece koyu zeminde**. Beyaz zeminde metin wordmark (`Wordmark`). Logo 16-48 px'te okunmadığı için favicon ve uygulama ikonları ayrı **işaretten**: `public/marka/isaret.svg` (siyah yuvarlak köşeli kare, beyaz "K", mavi "F"; harfler karenin yaklaşık %57'si, 2026-10-21). Sekme başlığı "Sayfa | KobiFabrika", ana sayfa ve Open Graph "KobiFabrika | KOBİ'nin dijital fabrikası" (`content/site.ts` → `homeTitle`). Yeniden üretim: `npm run marka` (README).
Slogan: **"KOBİ'nin dijital fabrikası"** (sayfa başlığı, meta açıklama, tanıtım sitesi, giriş ekranı marka paneli; tek kaynak `frontend/src/content/site.ts` → `siteSlogan`). Eski "işletim sistemi" ifadesi kullanılmaz (taslaklarda geçer). Sosyal medya hesapları (`siteSocial`) site alt bilgisinde ve İletişim sayfasında tek renk çizgi ikonlarla, 44 px dokunma alanı, yeni sekmede.

## Renkler (kesin — 2026-10-17, panel yenilemesi)

| Token | Hex | Kullanım |
|---|---|---|
| `black` | `#000000` | Yazı |
| `white` | `#ffffff` | Zemin, koyu zeminde yazı |
| `brand` | `#00b0ff` | Vurgu mavisi: **ana düğme (yazı siyah)**, aktif menü zemini, şerit, ikon, koyu zeminde link/yazı |
| `link` | `#006ea6` | Beyazda link ve vurgulu yazı (5,5:1), seçili sekme/çip, açık anahtar, kart ikonu |
| `navy-950` | `#000a1d` | Koyu yüzey: sol menü, üst çubuk, sitenin üst menüsü/alt bilgisi/koyu bölümleri, giriş ekranı marka paneli, odak halkası (beyaz kapsamda) |
| `navy-800` | `#00385a` | **Sadece koyu zeminde:** kart, hover, ayırıcı |
| `cyan` | `#00fdff` | **Sadece koyu zeminde** küçük vurgu (etiket, numara, ✓) |

- **Griler** (`neutral-50` … `neutral-900`) renksiz (R = G = B). Ayırıcı `neutral-200`, ikincil metin `neutral-600`, input kenarlığı `neutral-400`, koyu zeminde ikincil metin `neutral-300`.
- **Kırmızı** (`danger-600` #b91c1c, zemini `danger-50`) sadece hata ve silme.
- **Başarı için yeşil, uyarı için sarı yok:** ✓ ikonu; uyarı gri kutu + solda ince mavi çizgi.
- Token'lar: `frontend/src/app/globals.css`. Tailwind'in hazır paleti kapalı. Panel ve site aynı token'ları kullanır.

### Kontrast kuralları (`src/lib/theme/contrast.test.ts` hepsini token değerleriyle doğrular)

- Beyaz zeminde yazı: siyah, `neutral-600`/`700`, `link` (#006ea6). **`brand` ve `cyan` beyazda yazı olamaz** (2,1:1 ve 1,2:1); `brand` beyazda düğme zemini, şerit, ikon, alt çizgi olabilir.
- Koyu zeminde (`navy-950`, `navy-800`): beyaz, `neutral-300`, `brand`, `cyan` yazı olabilir.
- Düğmeler: mavi (`brand`) + siyah yazı (ana düğme); `link` veya lacivert zemin + beyaz yazı (seçili çip, numara rozeti).
- Odak: beyaz kapsamda lacivert halka (`navy-950`; mavi düğmenin yanında da 3:1), koyu kapsamda mavi halka.
- `danger-600` koyu zeminde yazı olamaz; koyu zeminde hata mesajı açık kutuda.

### Kapsamlar

- **Beyaz (`:root`):** panel içerik alanı, giriş/kayıt formları, kurulum, sitenin beyaz bölümleri. Ana düğme mavi (`<Button>`).
- **Koyu (`.dark` sınıfı, #000a1d):** panelin üst çubuğu ve sol menüsü (`sidebar-*`: aktif öğe mavi zemin + siyah yazı, hover lacivert #00385a), sitenin üst menüsü, alt bilgisi ve koyu bölümleri, giriş ekranının marka paneli. İçindeki semantik sınıflar kendiliğinden koyu zemine döner.
- **Bugün kartları:** üstte 4 px renkli şerit (`brand`, `link`, `navy-800`, `navy-950`) ve aynı tonda küçük ikon; yazı siyah.
- **Boş durumlar:** sade SVG çizim (`EmptyState`: lacivert çizgili boş sayfa + mavi dairede ekranın ikonu).
- Giriş ekranları: solda koyu lacivert marka paneli (logo, slogan, şirket unvanı), sağda beyaz form; 768px altında marka paneli üstte ince bant.

### Tanıtım sitesi

- Sıra: giriş (koyu, gerçek ekran görüntüsü laptop + telefon çerçevesinde) → kimler için (kendi SVG çizgi ikonları) → özellikler (para, müşteri, ekip) → farkımız (koyu) → nasıl çalışır (3 adım, büyük numaralar) → fiyatlar (Aylık/Yıllık) → SSS → son çağrı (koyu).
- Ana çağrı her yerde **"Ücretsiz dene"**; telefonda sayfanın altında sabit. Sayfalar: `/fiyatlar`, `/sss`, `/hakkimizda`, `/iletisim`, `/yardim`, hukuki metinler.
- Görseller demo işletmenin gerçek ekranları (`npm run demo:shots`, WebP), `next/image` ile; her görselde ne gösterdiğini anlatan alt metin; çerçeveler kendi CSS'imiz. Stok fotoğraf, uydurma yorum/sayı yok.
- Animasyon yok: giriş bölümündeki belirme de kaldırıldı (ilk ekranın en büyük öğesi geç boyanmış sayılıyordu; Lighthouse LCP, 2026-10-21).

## Tasarım ilkeleri (site ve panel)

Rakip incelemesi: `docs/rakip-site-notlari.md`.

- Ağırlıklı beyaz zemin, siyah yazı, bol boşluk, güçlü tipografi hiyerarşisi, **sola hizalı** düzen.
- **Mavi ölçülü:** her bölümde bir ana mavi öğe (ana düğme ya da vurgu); link mavisi #006ea6 yazı ve seçili durumlar için.
- Bölümleri ayırmak için **gölge yerine ince çizgi** (`border-black` güçlü ayrım, `border-neutral-200` hafif). Kartlarda gölge yok.
- **Ürünü göster:** sitedeki ekranlar demo işletmenin ("Işıltı Kuaför") gerçek ekran görüntüleri, kendi laptop ve telefon çerçevelerimizde; altında "Örnek ekran" yazar.
- **Metin esnafın dilinde,** somut ve kısa: "Veresiye defterini kapat", "Gelmeyen müşteriyi geri çağır". Kalıp sözler yok: "devrim", "akıllı", "yapay zekâ destekli", "işinizi bir üst seviyeye taşıyın".
- **Yasak:** gradyan, neon ışıma, cam/blur kart, yüzen/eğik 3D kart, emoji, her bölümde ikon yağmuru, her şeyin ortalanması, her kartın aynı yuvarlak köşe + gölge alması, sayaç animasyonu, uydurma müşteri yorumu/logo/kullanıcı sayısı.
- İkonlar tek renk (siyah; siyah zeminde beyaz/gri) ve az; liste maddelerinde ✓ yeterli.
- **Animasyon yok** (sitede de; açılır pencerelerin kendi geçişleri dışında).
- Köşe yuvarlaklığı 6px (`--radius`); büyük yüzeylerde (çerçeve, tablo) köşe yok ya da az.
- Paket fiyatları ve limitleri sitede `frontend/src/content/plans.ts`, uygulamada Plans tablosu (süper admin); ikisi birlikte güncellenir.

## Hitap ve sayı alanları (2026-10-21)

- **Hitap:** panel, giriş/kayıt/davet, kurulum, müşteriye açık sayfalar (online randevu, ekstre, teklif, anket) ve bütün e-postalar **"siz"** ("Kaydedin", "E-postanızı doğrulayın"). Tanıtım sitesi "sen". Düğme etiketleri kısa emir ("Kaydet", "Gönder") her iki hitapta da geçerli. `src/lib/hitap.test.ts` kesin "sen" kalıplarını yakalar.
- **Sayı alanları:** para, miktar, gün, yüzde alanları `NumberInput` (`components/ui/number-input.tsx`): harf girilemez; virgül ondalık, nokta binlik (virgülsüz "12.5" gibi son noktadan sonra 1-2 rakam ondalık sayılır); yazarken binlikler gruplanır, odaktan çıkınca para alanı "32.500,00" olur; telefonda sayısal klavye. `type="number"` kullanılmaz (Safari harf kabul eder, tekerlek değeri değiştirir).

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
