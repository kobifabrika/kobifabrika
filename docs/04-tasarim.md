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

## Tipografi

Sistem fontu yığını: `-apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif` (Türkçe karakter sorunu yok, hızlı).
Ölçek: 12 / 14 / 16 / 18 / 20 / 24 / 30 px. Rakamlar `tabular-nums`.

## Taslaklardan alınan dil

- Minimal, sakin, beyaz kartlar, ince kenarlıklar, 6-8px köşe yuvarlaklığı, gölge az.
- Giriş ekranı: solda koyu marka paneli (KobiFabrika, slogan, şirket unvanı), sağda beyaz form (kullanıcı adı/e-posta, şifre + Göster, Beni hatırla, Şifremi unuttum, Giriş Yap, Üye Ol).
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
