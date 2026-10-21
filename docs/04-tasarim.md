# Tasarım

Referans taslaklar: `docs/tasarim-referans/index.html` (giriş ekranı) ve `admin.html` (panel).
Bu dosyalar sadece görsel referanstır; içlerindeki "KOBIOS" adı eski çalışma adıdır, her yerde **KobiFabrika** yazılacak.
**Logo (tek logo, 2026-10-21):** yazı logosu **"KobiFabrika"**: "Kobi" ve "Fabrika" farklı renkte, **alt çizgi yok**, Schibsted Grotesk ExtraBold (800), harf aralığı -0,025em (sitenin başlık fontu). Harfler vektör yola çevrilmiş iki SVG: `public/marka/logo-koyu.svg` (koyu zemin: "Kobi" beyaz, "Fabrika" #00b0ff) ve `logo-acik.svg` (açık zemin: "Kobi" #000a1d, "Fabrika" #00b0ff); oran 586:81. Her yerde tek bileşen: `components/brand/logo.tsx` → `<Logo surface="light" | "dark" />` (yükseklik className ile). Kullanıldığı yerler: site üst menüsü ve alt bilgisi (18 px), telefondaki site menüsü, giriş/kayıt/şifre/davet marka paneli (telefonda 16, masaüstünde 40 px), kurulum, panel yükleniyor ekranı, panel sol menüsünün altı ve telefondaki Menü (14 px), /platform, 404 ve hata sayfaları, online randevu ve müşteriye açık sayfaların alt satırı ("[logo] ile online randevu", 10 px), Open Graph görseli. E-postalarda font/SVG gösterilemediği için açık zemin logosunun beyaz zeminli 2x PNG'si (`logo-eposta.png`, 180 × 25 px gösterim, alt metin "KobiFabrika"). **Logo kontrast kuralından muaftır** (açık zemindeki mavi "Fabrika" 2,1:1); başka hiçbir yazı bu muafiyeti kullanamaz. Favicon ve uygulama ikonları ayrı **işaretten**: `public/marka/isaret.svg` (#000a1d yuvarlak köşeli kare, beyaz "K", mavi "F"; harfler logoyla aynı fonttan, ikisi birlikte karenin genişliğinin %50'si). Hepsi tek komutla üretilir: `npm run marka` (font `scripts/fonts/`, OFL); 16, 32, 180 px ikonların 8 kat ve logonun 12/24 px büyütülmüş kontrol görüntüleri `test-results/marka/`. Sekme başlığı "Sayfa | KobiFabrika", ana sayfa ve Open Graph "KobiFabrika | KOBİ'nin dijital fabrikası" (`content/site.ts` → `homeTitle`).
Slogan: **"KOBİ'nin dijital fabrikası"** (sayfa başlığı, meta açıklama, tanıtım sitesi, giriş ekranı marka paneli; tek kaynak `frontend/src/content/site.ts` → `siteSlogan`). Eski "işletim sistemi" ifadesi kullanılmaz (taslaklarda geçer). Sosyal medya hesapları (`siteSocial`) site alt bilgisinde ve İletişim sayfasında tek renk çizgi ikonlarla, 44 px dokunma alanı, yeni sekmede.

## Renkler (kesin — site ve panel tek palet, 2026-10-21)

| Token | Hex | Kullanım |
|---|---|---|
| `black` | `#000000` | Yazı |
| `white` | `#ffffff` | Zemin, koyu zeminde yazı |
| `brand` | `#00b0ff` | Vurgu mavisi: **ana düğme (yazı #000a1d)**, aktif menü zemini, şerit, ikon, seçili sekme alt çizgisi, logodaki "Fabrika", koyu zeminde link/yazı |
| `link` | `#006ea6` | Beyazda link ve vurgulu yazı (5,5:1), açık anahtar, kart ikonu |
| `navy-950` | `#000a1d` | **Ana koyu renk (bütün ürün):** sol menü, üst çubuk, sitenin koyu bantları ("Telefondan yönet", "İşin listede yok mu?", "Defteri bugün kapat"), giriş/kayıt marka paneli, /platform ve durum sayfası başlığı; sitede yazı rengi (ana sayfa başlığı dahil); ana düğme yazısı; seçili çip/hap sekme; odak halkası (beyaz kapsamda). Eski site tonu #0a1f3c kalktı |
| `navy-800` | `#00385a` | **Sadece koyu zeminde:** kart, hover, ayırıcı |
| `cyan` | `#00fdff` | **Sadece koyu zeminde** küçük vurgu (etiket, numara, ✓) |

- **Griler** (`neutral-50` … `neutral-700`) sitenin mavi-gri ailesi (2026-10-21; önceden renksiz griydi): `50` #f9fafd, `100` #f4f8fc (açık zemin: sitede bölüm/alt bilgi, panelde ikincil yüzey/hover), `200` #e3e9f1 (kenarlık, ayırıcı), `300` #c6d2e1 (koyu zeminde ikincil yazı ve ikon), `400` #8490a2 (input kenarlığı 3:1, koyu zeminde soluk yazı), `500` #637186, `600` #4a5a70 (ikincil yazı), `700` #303b49. Site ve panel aynı adları kullanır (sitenin eski `ink`, `slate`, `slate-200/300`, `mist` token'ları bunlara taşındı).
- **Tek kaynak:** bütün renkler `frontend/src/app/globals.css` (`@theme`); kodda hex yok. CSS dışında renk gereken yerler (Open Graph, manifest, tarayıcı tema rengi) `src/lib/theme/colors.ts`'ten okur, `colors.test.ts` değerlerin aynı olduğunu doğrular. Backend e-posta (`EmailTemplates`) ve PDF (`StatementPdf`) sabitleri aynı değerlerde.
- **Kırmızı** (`danger-600` #b91c1c, zemini `danger-50`) sadece hata ve silme.
- **Başarı için yeşil, uyarı için sarı yok:** ✓ ikonu; uyarı gri kutu + solda ince mavi çizgi.
- Tailwind'in hazır paleti kapalı. Panel, site, /platform ve müşteriye açık sayfalar aynı token'ları kullanır. Mercan (`coral*`, `cream`) sadece tanıtım sitesinde.

### Kontrast kuralları (`src/lib/theme/contrast.test.ts` hepsini token değerleriyle doğrular)

- Beyaz zeminde yazı: siyah, `neutral-600`/`700`, `link` (#006ea6). **`brand` ve `cyan` beyazda yazı olamaz** (2,1:1 ve 1,2:1); `brand` beyazda düğme zemini, şerit, ikon, alt çizgi olabilir.
- Koyu zeminde (`navy-950`, `navy-800`): beyaz, `neutral-300`, `brand`, `cyan` yazı olabilir.
- Düğmeler: mavi (`brand`) + #000a1d yazı (ana düğme); #000a1d zemin + beyaz yazı (seçili çip, hap sekme); `link` zemin + beyaz yazı (numara rozeti).
- **Logo** bu kurallardan muaf (yukarıda); test aynı dosyada logonun renklerini ayrıca doğrular.
- Odak: beyaz kapsamda lacivert halka (`navy-950`; mavi düğmenin yanında da 3:1), koyu kapsamda mavi halka.
- `danger-600` koyu zeminde yazı olamaz; koyu zeminde hata mesajı açık kutuda.

### Kapsamlar

- **Beyaz (`:root`):** panel içerik alanı, giriş/kayıt formları, kurulum, sitenin beyaz bölümleri. Ana düğme mavi (`<Button>`).
- **Koyu (`.dark` sınıfı, #000a1d):** panelin üst çubuğu ve sol menüsü (`sidebar-*`: aktif öğe mavi zemin + #000a1d yazı, hover lacivert #00385a, ayırıcı çizgi beyazın %12'si #000a1d'ye karışık soluk ton; mavi çizgi yok), sitenin koyu bantları, giriş ekranının marka paneli, /platform ve durum sayfası başlığı. İçindeki semantik sınıflar kendiliğinden koyu zemine döner.
- **Bugün kartları:** üstte 4 px renkli şerit (`brand`, `link`, `navy-800`, `navy-950`) ve aynı tonda küçük ikon; yazı siyah.
- **Boş durumlar:** sade SVG çizim (`EmptyState`: lacivert çizgili boş sayfa + mavi dairede ekranın ikonu).
- Giriş ekranları: solda koyu marka paneli (#000a1d; büyük koyu zemin logosu, slogan, üç güvence; fotoğraf yok), sağda beyaz form; 768px altında marka paneli üstte ince bant (sadece logo).
- **Panel üst bölümü (2026-10-21):** sol menünün başlığı (işletme adı) ile üst çubuk **tek bant**: ikisi de #000a1d, masaüstünde 52 px, alt çizgileri aynı hizada ve aynı soluk renkte (`sidebar-border`). Telefonda üst çubuk 48 px, aynı ton ve tek çizgi. Arama kutusu 36 px: beyazın %10'u zemin, %15'i kenarlık, açık gri (`neutral-300`) yazı ve ikon, aynı dilde "Ctrl K" etiketi, odakta marka mavisi ince çerçeve. Sağda "Yeni" (ana düğme), yardım, zil ve avatar 36 px aynı hizada; ikon düğmelerinin hover'ı beyazın %10'u; avatar açık gri daire (`neutral-300`) + #000a1d baş harfler (koyu zeminde kaybolmaz). Sol menünün altındaki logo koyu zemin SVG'si, 14 px yükseklik, API durum satırıyla aynı sol kenarda.
- **Bugün hızlı işlemleri** (Randevu ver, Satış yap, Tahsilat al, Gider ekle) dördü aynı görünümde (çerçeveli beyaz; mavi olan seçili sanılıyordu), özet kartlarının hemen altında; telefonda da aynı sıra (alt çubuktaki + ayrıca hızlı erişim).

### Tanıtım sitesi (yenilendi 2026-10-21: ikas + Stripe + Paraşüt yönü; renkler, logo ve temel bileşenler panelle ortak)

Referans notları: `docs/tasarim-referanslari.md`. Kapsam: ana sayfa, fiyatlar, SSS, hakkımızda, iletişim, yardım, hukuki
sayfalar, giriş/kayıt ekranları. Panelin düzeni kendi kalır; renk, logo, yazı tipi ve temel bileşenler aynı aileden (aşağıda "Site ↔ panel bileşen eşleme").

Site, panelle aynı token'ları kullanır (yukarıdaki tablo). Sitede nasıl kullanıldıkları ve siteye özel olanlar:

| Token | Hex | Kullanım |
|---|---|---|
| `navy-950` | `#000a1d` | (ortak) Sitede **yazı rengi**, ana sayfa başlığı, seçili hap sekme/dönem; zemin olarak koyu bantlar ve giriş/kayıt paneli (üstünde beyaz, `neutral-300`, mavi, mercan yazı; link mavisi ve `coral-text` olmaz) |
| `neutral-600` / `-200` / `-100` | `#4a5a70` / `#e3e9f1` / `#f4f8fc` | (ortak) İkincil yazı / kenarlık / açık bölüm zemini, alt bilgi, alt sayfa başlık bandı |
| `cream` | `#fff5f0` | (sadece site) Sıcak açık zemin (ana sayfada 2026-10-21'den beri kullanılmıyor; diğer sayfalar) |
| `coral` | `#ff6b4a` | (sadece site) **Sıcak yardımcı renk:** ilk ekran geçişi; beyazda yazı **olamaz** |
| `coral-soft` | `#ffe1d6` | (sadece site) Rozet ve ✓ dairesi zemini (yazı `navy-950`) |
| `coral-text` | `#b9381c` | (sadece site) Beyaz/kremde mercan yazı (numara, ikincil link) |

- **Üst menü** beyaz, ince çizgi, sabit: açık zemin logosu (`<Logo surface="light">`, 18 px; KF işareti yok), az menü (Özellikler `/ozellikler`, Fiyatlar, SSS, Yardım, Hakkımızda), "Giriş yap", mavi "Ücretsiz dene". Telefonda Menü sağda, "Ücretsiz dene" altta sabit (beyaz şerit).
- **İlk ekran:** tek yerde **ölçülü renk geçişi** (`hero-glow`: sis → beyaz, sağ üstte düşük yoğunluklu mavi ve mercan); sola
  hizalı büyük başlık (Schibsted Grotesk 600), iki düğme, ✓'li dört güvence; sağda kafe fotoğrafı (dış masada müşterilerden sipariş
  alan önlüklü garson) + telefondaki gerçek ekran. Başlığın tamamı ana koyu renkte (`navy-950`, #000a1d).
  Başka hiçbir bölümde geçiş yok.
- **Bölümler:** kimler için (fotoğraflı sektör kartları) → özellikler (kısa hali: Para · Müşteri · Ekip sekmeleri, çerçevede gerçek
  ekran; `FeatureTabs`; "Bütün özellikler" → `/ozellikler`) → telefondan yönet (#000a1d bant, fotoğraf + online randevu ekranı; mavi ikon kutuları) → farkımız → nasıl çalışır (sis bant) →
  fiyatlar → SSS → son çağrı (#000a1d bant + fotoğraf) → kolonlu alt bilgi (`neutral-100` zemin, açık zemin logosu; Ürün, Destek, Şirket, Hukuki).
- **Özellikler sayfası** `/ozellikler` ("Özellikler | KobiFabrika"): modül çipleri, modül başına başlık + çerçevede ekran + ayrıntılı
  özellik listesi (`content/site.ts` → `featureDetails`; sadece ürünün bugün yaptıkları, SMS'e dayananlar yok), telefon ekranları,
  son çağrı. Eski `/#ozellikler` istemcide `/ozellikler`'e yönlenir (`HashRedirect`).
- **Fotoğraf:** Pexels (lisans ve kaynaklar `docs/foto-kaynaklari.md`), sadece atmosfer: kişiye isim, yorum, "kullanıcımız"
  yazılmaz; görünür marka/logo yok; sahneyi anlatan alt metin. **Kalite:** net, aydınlık, profesyonel; orijinalinden kırpılır,
  gösterim boyutunun en az 2 katı (ilk ekran 2400 px), WebP q85 (`npm run site:foto`); `next/image` kalite 85 ve doğru `sizes`. Müşteri yorumu bölümü yok (pilot yorumları
  gelince eklenir). Ürün ekranları demo işletmenin güncel panelden gerçek görüntüleri, masaüstü ve telefon 2x (`npm run demo:shots`; panel
  tasarımı değişince yeniden çalıştırılır), `BrowserFrame`/`PhoneFrame` içinde.
- **Kart ve gölge:** köşe 12 px (`rounded-2xl`; fotoğraflar 18 px), ince `neutral-200` kenarlık; sadece ürün çerçeveleri, öne çıkan
  fiyat kartı ve büyük fotoğraflarda **çok hafif yumuşak gölge** (`softShadow`). Neon, ışıma, cam/blur yok.
- **Düğmeler:** ana düğme mavi + #000a1d yazı; ikincil beyaz + ince kenarlık; dönem ve modül sekmeleri hap biçimi (seçili `navy-950`).
- **Hitap** sitede "sen", giriş/kayıt ekranlarında "siz". Animasyon yok (açılır pencerelerin kendi geçişleri dışında).
- **Sekme başlığı:** "Sayfa | KobiFabrika"; ana sayfa ve Open Graph "KobiFabrika | KOBİ'nin dijital fabrikası".

## Site ↔ panel bileşen eşleme (2026-10-21)

Amaç "aynı firmanın ürünü" hissi; panelin düzeni (menü yapısı, sayfa iskeleti, kartların ve düğmelerin yeri, koyu sol menü)
değişmedi. Farklı olanlar sitedeki hale eşlendi. Ortak bileşenler `components/ui/` (shadcn), token'lar `globals.css`.

| Bileşen | Site | Panel (önce) | Yapılan |
|---|---|---|---|
| Ana koyu renk | #0a1f3c (`ink`) | #000a1d | Site #000a1d'ye döndü (yazı ve koyu yüzeyler); tek token `navy-950` |
| Griler | mavi-gri (`slate` #4a5a70, `slate-200` #e3e9f1, `mist` #f4f8fc) | renksiz gri (#525252, #e5e5e5, #f2f2f2) | Panel sitedeki değerlere eşlendi; tek ad `neutral-*` |
| Logo | metin "KobiFabrika" (açıkta "Fabrika"nın altı mavi çizgili) | potrace'lenmiş "KOBI FABRIKA" SVG | Tek vektör logo, `Logo surface` (yukarıda) |
| Yazı tipi | başlık Schibsted Grotesk, gövde sistem fontu | aynı | Aynıydı; font tek yerde (`app/layout.tsx`, `next/font`) bir kez yüklenir; boyut/kalınlık panelde aynı |
| Ana düğme | `Button`: mavi, yazı #0a1f3c (sınıfla ezilmiş), köşe 6 px, 44/48 px, hover %80 mavi, odak #000a1d halka | aynı bileşen, yazı siyah | Yazı rengi token'da #000a1d (`--primary-foreground`); sitedeki sınıf ezmeleri kalktı. "Yeni" düğmesine aynı hover |
| İkincil düğme | `Button variant="outline"`: beyaz, ince kenarlık, hover açık zemin | aynı bileşen | Kenarlık/hover rengi griler eşlenince aynı (#e3e9f1, #f4f8fc) |
| Form alanları | `Input`: kenarlık `neutral-400`, köşe 6 px, odakta koyu kenar + 3 px halka, yer tutucu `neutral-600`, hata kırmızı kenar | aynı bileşen | Aynıydı; kenarlık ve yer tutucu mavi-gri aileye geçti |
| Kartlar | köşe 12 px, `neutral-200` kenarlık; gölge sadece ürün çerçevesi, öne çıkan fiyat kartı, fotoğraf | `Card`: köşe 9 px, siyah %10 halka | `Card` köşe 12 px, kenarlık `border` (#e3e9f1); panelde gölge eklenmedi |
| Seçili çip / hap sekme | #0a1f3c zemin + beyaz yazı; hover'da kenarlık koyu | `link` #006ea6 zemin + beyaz | Panel çipleri (`Chip`, `ChipLink`, ödeme yöntemi, takvim, katalog, gider kategorisi, içe aktarma, online randevu gün/saat) #000a1d zemin + beyaz, hover kenarlık #000a1d |
| Alt çizgili sekme | üst menüde seçili: koyu yazı + 2 px mavi alt çizgi | `TabNav`: link mavisi yazı ve çizgi | `TabNav` seçili koyu yazı + mavi (`brand`) çizgi |
| Rozet | `coral-soft` + koyu yazı (sadece site) | `Badge` (shadcn) | Değişmedi; mercan panele girmedi |
| Link | metin içinde `text-link` + alt çizgi; menü/alt bilgi linkleri çizgisiz, hover'da çizgi | metin içinde `text-link` + alt çizgi | Aynıydı |
| İkon | lucide, çizgi 2 (✓ listelerinde 2,75) | lucide, çizgi 2 | Aynıydı |
| Odak halkası | beyazda #000a1d 3 px, koyuda mavi | aynı (`--ring`) | Aynıydı (tek kural `globals.css`); arama kutusunda ince mavi çerçeve |

## Tasarım ilkeleri (site ve panel)

Rakip incelemesi: `docs/rakip-site-notlari.md`.

- Ağırlıklı beyaz zemin, siyah yazı, bol boşluk, güçlü tipografi hiyerarşisi, **sola hizalı** düzen.
- **Mavi ölçülü:** her bölümde bir ana mavi öğe (ana düğme ya da vurgu); link mavisi #006ea6 yazı ve seçili durumlar için.
- Bölümleri ayırmak için **gölge yerine ince çizgi** (`border-black` güçlü ayrım, `border-neutral-200` hafif). Kartlarda gölge yok (sitede ürün çerçevesi, öne çıkan fiyat kartı ve büyük fotoğrafta çok hafif yumuşak gölge serbest).
- **Ürünü göster:** sitedeki ekranlar demo işletmenin ("Işıltı Kuaför") gerçek ekran görüntüleri, kendi laptop ve telefon çerçevelerimizde; altında "Örnek ekran" yazar.
- **Metin esnafın dilinde,** somut ve kısa: "Veresiye defterini kapat", "Gelmeyen müşteriyi geri çağır". Kalıp sözler yok: "devrim", "akıllı", "yapay zekâ destekli", "işinizi bir üst seviyeye taşıyın".
- **Yasak:** gradyan (tek istisna: sitenin ilk ekranındaki ölçülü `hero-glow`), neon ışıma, cam/blur kart, yüzen/eğik 3D kart, emoji, her bölümde ikon yağmuru, her şeyin ortalanması, her kartın aynı yuvarlak köşe + gölge alması, sayaç animasyonu, uydurma müşteri yorumu/logo/kullanıcı sayısı.
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
