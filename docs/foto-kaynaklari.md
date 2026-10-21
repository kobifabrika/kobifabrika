# Fotoğraf kaynakları (tanıtım sitesi)

Bütün fotoğraflar **Pexels**'ten, **Pexels Lisansı** ile (https://www.pexels.com/license/): ticari kullanım ücretsiz, atıf
zorunlu değil, düzenlenebilir. Lisansın yasakladıkları: fotoğrafı değiştirmeden satmak, kişileri olumsuz/yanıltıcı biçimde
göstermek, fotoğraftaki kişinin bir ürünü onayladığını ima etmek.

Kurallar (karar 2026-10-21, kalite turu aynı gün):
- Fotoğraflar **sadece atmosfer** için. Kişilere isim, yorum ya da "kullanıcımız" ifadesi yazılmaz; alt metin sahneyi anlatır.
- Görünür marka veya logo olan fotoğraf kullanılmaz; yazılı tabela, neon yazı, pelerin/önlük yazısı olan kısımlar kırpılır.
- **Kalite:** net, aydınlık, profesyonel; bulanık, karanlık, düşük çözünürlüklü ya da sahnelenmiş/yapay duranlar elenir. Her
  fotoğraf Pexels'in **orijinal** boyutundan kırpılır ve gösterildiği en büyük boyutun en az 2 katı genişlikte yazılır (ilk ekran
  2400 px, sektör kartları 1600 px, "Telefondan yönet" 1600 px, son çağrı 2400 px); WebP kalite 85. Sitede `next/image`
  (kalite 85, ekran genişliğine göre `sizes`; retina ekranda 2x kendiliğinden seçilir).
- Üretim tek komut: `cd frontend; npm run site:foto` (`scripts/site-foto.mjs`; liste ve kırpmalar orada, bu tabloyla aynı tutulur).
- Unsplash denendi; arama sayfası ve API otomatik erişime kapalı olduğu için Pexels kullanıldı.

| Dosya | Nerede | Sahne | Pexels sayfası | Fotoğrafçı | Düzenleme |
|---|---|---|---|---|---|
| `kafe-siparis.webp` | İlk ekran | Kafenin dış masasında iki müşteriden sipariş alan önlüklü garson | https://www.pexels.com/photo/waiter-getting-the-customer-s-orders-4921154/ | RDNE Stock project | Soldaki bitki kısmen kırpıldı (4:3,4), 2400 px |
| `berber-makine.webp` | Kuaför ve berber | Müşterisinin saçını makineyle kesen berber | https://www.pexels.com/photo/a-barber-holding-a-hair-clipper-7697224/ | RDNE Stock project | Dikeyden 4:3 kırpma (iki yüz kadrajda) |
| `guzellik.webp` | Güzellik ve bakım | Müşterisine makyaj yapmaya hazırlanan makyöz | https://www.pexels.com/photo/woman-with-cloak-on-shoulder-in-beautician-20309789/ | Tatiana Lucky | 4:3 kırpma |
| `pilates.webp` | Pilates ve spor stüdyosu | Reformer aletinde üyesine yardım eden eğitmen | https://www.pexels.com/photo/fitness-instructor-assisting-woman-doing-pilates-9288101/ | Nicholas Fu | 4:3 kırpma |
| `restoran.webp` | Kafe ve restoran | Dolu restoranda masalara salata tabakları taşıyan garson | https://www.pexels.com/photo/a-waiter-holding-plates-with-salad-25809274/ | Matheus Bertelli | 4:3 kırpma |
| `cicekci.webp` | Dükkân ve mağaza | Çiçekçide buket hazırlayan kadın | https://www.pexels.com/photo/happy-woman-working-in-flower-shop-5409710/ | Amina Filkins | Üstteki el yazısı tabela kırpıldı (4:3) |
| `kuafor.webp` | Telefondan yönet | Aydınlık salonda müşterisinin saçını boyayan kuaför | https://www.pexels.com/photo/professional-hair-coloring-in-modern-salon-35225425/ | Göksun Barış Gökalp | Alttaki pelerin yazısı kırpıldı (4:5) |
| `servis.webp` | Son çağrı | Kafede müşteriye servis | https://www.pexels.com/photo/cheerful-waitress-serving-dish-to-customer-in-cafe-4350094/ | Ketut Subiyanto | 3:2 kırpma |

Elenenler: kasada tablet kullanan kadın (fotoğrafçısı bir kasa yazılımı markası, ekranda ürün görünebilir), raflarında çok sayıda
markalı ürün olan bakkal, yeşil önlüklü zincir kafe görüntüsü, neon/tabela yazılı kafeler. 2026-10-21 kalite turunda çıkarılanlar:
eski ilk ekran kuaför fotoğrafı (7755223; pelerin yazısı ve arkada kırmızı tabelalar), karanlık ve neon yazılı "telefonla sipariş"
(6612701), sahnelenmiş/yapay duran "kafe sahibi" (36729739), tuhaf kadrajlı manikür yakın çekimi (19239100), kadrajı zayıf
pilates (25599833).

2026-10-21 defter rengi turunda çıkarılanlar: ilk ekran barista (5710456; kafe olduğu yeterince anlaşılmıyordu), kafe kartı
barista (3914062) ve berber (9992819), Emir'in isteğiyle daha anlaşılır sahnelerle değişti. Bu turda elenen adaylar: makinesinde
ve önlüğünde marka yazısı olan barista sahneleri (40025142, 20344562), tabela/neon yazılı tezgâhlar (6612738, 31353701), loş veya
sahnelenmiş duran restoran (3770095).
