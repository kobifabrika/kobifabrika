# Tasarım

Referans taslaklar: `docs/tasarim-referans/index.html` (giriş ekranı) ve `admin.html` (panel).
Bu dosyalar sadece görsel referanstır; içlerindeki "KOBIOS" adı eski çalışma adıdır, her yerde **KobiFabrika** yazılacak.
Logo henüz yok: logo gelene kadar "KobiFabrika" yazısı (wordmark) kullanılır.

## Renkler (site geneli, kesin)

| Token | Hex | Kullanım |
|---|---|---|
| `brand-900` (color1) | `#000a1d` | En koyu lacivert: sol menü zemini, başlıklar, ana metin, giriş ekranı marka paneli |
| `brand-700` (color2) | `#00385a` | İkincil koyu: hover, kart başlıkları, tablo başlığı |
| `brand-500` (color3) | `#006ea6` | Ana buton (beyaz yazı), link, beyaz zeminde vurgu metni |
| `brand-300` (color4) | `#00b0ff` | Koyu zeminde aktif menü, ikon, grafik çizgisi, rozet; açık buton (yazı `#000a1d`) |
| `brand-100` (color5) | `#00fdff` | Koyu zeminde vurgu, odak halkası, grafik ikincil |

Nötrler: hafif lacivert tonlu griler (ör. zemin `#f5f7fa`, kenarlık `#e3e8ef`, ikincil metin `#5b6676`).
Durum renkleri (marka renklerinden ayrı): başarı `#15803d`, uyarı `#b45309`, hata `#b91c1c` (açık zeminleri `#ecfdf3`, `#fffbeb`, `#fef2f2`).
Odak halkası: açık zeminde `brand-500`, koyu zeminde `brand-100`.
Token'ların kodu: `frontend/src/app/globals.css` (Tailwind sınıfları: `bg-brand-900`, `text-brand-500`, `border-neutral-200`, `text-danger-600`…).

### Kontrast kuralları

- Beyaz zeminde yazı/link: sadece `#000a1d` (19.8:1), `#00385a` (12.3:1), `#006ea6` (5.6:1).
- `#00b0ff` (2.4:1) ve `#00fdff` (1.3:1) beyaz zeminde **yazı olarak kullanılmaz**; ikon, çizgi, dekor için.
- Koyu zeminde (`#000a1d`): `#00b0ff` (8.2:1) ve `#00fdff` (15.5:1) rahat okunur.
- `#00b0ff` buton → yazı `#000a1d`. `#006ea6` buton → yazı beyaz.

### Site ve kimlik ekranları koyu tema (2026-10-10)

- **Koyu:** tanıtım sitesi (`/`, `/kvkk`), giriş/kayıt/şifre/davet/e-posta doğrulama ekranları, kurulum sihirbazı (`/kurulum`), panelin sol menüsü ve üst çubuğu. **Açık:** panelin içerik alanı (ve alt sekme çubuğu).
- Uygulama: kapsayıcıya `dark` sınıfı verilir; `globals.css` → `.dark` bloğu semantik değişkenleri koyuya çevirir. Bileşenlerde sabit renk yerine semantik sınıf kullan (`bg-card`, `text-foreground`, `text-muted-foreground`, link için `text-primary`); böylece aynı bileşen panelde açık, kimlik ekranlarında koyu görünür. Koyu kapsam içinde açık bir ada gerekirse (ör. sitedeki panel önizlemesi) `light` sınıfı.
- Yeni renk yok. Zemin `#000a1d`; kart/yüzey `#00385a` ile `#000a1d` karışımı (`color-mix`) veya `#000a1d` üstünde %3-4 şeffaf beyaz (cam kart).
- Koyu zeminde yazı: başlık/ana metin beyaz, ikincil metin `neutral-300`/`neutral-400`; link ve vurgu `#00b0ff`, güçlü vurgu `#00fdff`. **`#006ea6` ve `neutral-600` koyu zeminde yazı olarak kullanılmaz.**
- Ana buton `#00b0ff` zemin + `#000a1d` yazı (hover `#00fdff`). Odak halkası `#00fdff`. Input koyu zemin (`#000a1d`), açık yazı, kenarlık `neutral-500`.
- Durum renkleri (`danger-600`, `success-600`) koyu zeminde yazı olamaz (3:1 altı): hata ve başarı mesajları açık kutuda (`bg-danger-50 text-danger-600`) gösterilir.
- Efektler ölçülü: hafif turkuaz ışıma, ince çizgi ızgara (`.kf-grid`), cam kart. Kaydırmada giriş animasyonu (`.kf-reveal`, saf CSS) sadece `prefers-reduced-motion: no-preference` iken.
- Paket fiyatları tek dosyada: `frontend/src/content/plans.ts` (`monthlyPrice: null` → "Lansman fiyatı yakında").
- Kontrast testleri (`src/lib/theme/contrast.test.ts`) `.dark` bloğunu okuyup koyu kombinasyonları da doğrular.

## Tipografi

Sistem fontu yığını: `-apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif` (Türkçe karakter sorunu yok, hızlı).
Ölçek: 12 / 14 / 16 / 18 / 20 / 24 / 30 px. Rakamlar `tabular-nums`.

## Taslaklardan alınan dil

- Minimal, sakin, beyaz kartlar, ince kenarlıklar, 6-8px köşe yuvarlaklığı, gölge az.
- Giriş ekranı: solda koyu marka paneli (KobiFabrika, slogan, şirket unvanı), sağda form (2026-10-10'dan beri form da koyu kart; yukarıya bak) (kullanıcı adı/e-posta, şifre + Göster, Beni hatırla, Şifremi unuttum, Giriş Yap, Üye Ol).
- Panel: koyu sol menü (YÖNETİM ve İŞLETME grupları, sayı rozetleri), üstte selamlama ("Günaydın, Emir."), 4 istatistik kartı, satış grafiği, personel durumu, son satışlar tablosu.
- Menü (Faz 1): **İşletme:** Bugün · Randevular · Satışlar · Müşteriler · Ürünler/Hizmetler · Stok · Cari ve Kasa · Kampanyalar. **Yönetim:** Personel · Mesai ve İzin · Raporlar · Ayarlar.

## Taslaklardaki hatalar (tekrarlanmayacak)

1. Giriş ekranı telefonda da 50/50 yan yana kalıyor → 768px altında marka paneli üstte ince bant, form tam genişlik.
2. 6-11px yazılar → en küçük 12px, input yazısı 16px (iPhone 16px altındaki inputlarda sayfayı yakınlaştırır).
3. `overflow: hidden` ile kaydırma kapatılmış → klavye açılınca form kesilmesin, kaydırma açık.
4. Unicode menü ikonları → lucide-react ikonları.

## Mobil

- 375px genişlikte her ekran kullanılabilir olmalı.
- Telefonda alt sekme çubuğu: Bugün, Randevu, Satış, Müşteri, Menü; ortada hızlı işlem (+) butonu (yeni satış / randevu / müşteri / tahsilat).
- Dokunma alanı en az 44×44px. Tablolar telefonda kart listesine dönüşür.
