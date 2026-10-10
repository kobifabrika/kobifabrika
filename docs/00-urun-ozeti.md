# KobiFabrika — ürün özeti

## Tek cümle

KOBİ ve esnafın parasını takip eden, müşteri getiren ve personelini yöneten tek uygulama.

## Hedef kitle

Türkiye'de 1-50 çalışanlı işletmeler. Öncelik: hizmet esnafı (kuaför, berber, güzellik salonu, klinik, spor salonu, oto servis) ve küçük perakende/toptan. İşini çoğunlukla telefondan yönetir, muhasebe bilgisi sınırlıdır, birden fazla programla uğraşmak istemez.

## Problem (rakip analizinden, 10.10.2026)

Pazar ikiye bölünmüş:

- Ön muhasebe programları (Paraşüt, Bizim Hesap, KolayBi, Logo İşbaşı, Mikro): randevu, SMS kampanyası, sadakat, mesai/izin yok.
- Randevu programları (Salon Randevu, Kolay Randevu, Fresha): cari hesap ve e-Fatura yok.

Esnaf ikisini ayrı alıyor (ayda 2.000 TL'nin üzerinde) ya da birinden vazgeçiyor. Bütün rakiplerin ortak şikayeti: geç ve şablon destek, habersiz otomatik yenileme, iade reddi, yüksek zamlar.

## Bizi farklı kılan (ürünün ve tanıtım sitesinin ana mesajı)

1. **Tek uygulama, iki iş:** para takibi + müşteri kazanma.
2. **Müşteri kazandıran program:** geri kazanma mesajı, boş saat kampanyası, memnuniyet → Google yorum.
3. **TL, sabit, komisyonsuz fiyat.**
4. **Şeffaf abonelik:** yenilemeden 7 gün önce hatırlatma, tek tıkla iptal, sürpriz zam yok.
5. **Veri senindir:** abonelik bitse de salt okunur erişim ve Excel dışa aktarma.
6. **Gerçek destek:** panelde WhatsApp destek; bayi ve aracı yok.
7. **Kalıcı ücretsiz paket (Çırak).**
8. **10 dakikada kurulum, telefondan kullanım.**
9. **Mali müşavir ücretsiz** (salt okunur kullanıcı).

## Paketler (10.10.2026 onaylandı)

| Paket | Fiyat (aylık, KDV hariç, yıllık ödemede) | Not |
|---|---|---|
| Çırak | Ücretsiz, süresiz | Sınırlı kullanım (limitler Sprint 8'de netleşecek) |
| Kalfa | 599-799 TL | Tek başına bir ön muhasebe programından ucuz |
| Usta | 1.199-1.499 TL | Ön muhasebe + randevu programının toplamından ucuz |

Kesin fiyat lansman öncesi bu aralıkta belirlenecek. 14 gün deneme, iyzico ile kartla ödeme. SMS kredisi ayrı satılır.

## Fazlar

- **Faz 1:** `docs/01-faz1-kapsam.md` (onaylandı).
- **Faz 2:** e-Fatura/e-Arşiv (entegratör API), sanal POS ile online tahsilat ve randevuda kapora, banka hareketlerini otomatik eşleştirme, fiş fotoğrafından gider kaydı, müşteri portalı, WhatsApp Business API, AI asistan, sadakat ve kupon, satın alma, varyant ve çoklu depo, fırsat panosu, görev takibi, gelişmiş raporlar.
- **İleride:** işletmeye web sitesi oluşturma, bordro ve SGK, çek/senet, çoklu şube, e-ticaret ve pazaryeri entegrasyonu.

## Alan adları

- kobifabrika.com: tanıtım ve kayıt sitesi
- app.kobifabrika.com: işletme paneli
- api.kobifabrika.com: API
- kobifabrika.com/r/{isletme-slug}: işletmenin online randevu sayfası
