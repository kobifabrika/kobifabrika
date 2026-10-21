# Renk yönü seçenekleri (öneri, 2026-10-21)

Emir'in yorumu: "Site ve panel çok soğuk. Sade ama daha canlı renkler istiyorum; premium bir site havası versin."
Bu klasör **sadece öneri**; uygulama koduna bir şey uygulanmadı. Seçim yapılınca seçilen tema `globals.css` token'larına,
`docs/04-tasarim.md`'ye ve `contrast.test.ts`'e işlenir.

- `karsilastirma.webp`: tek görselde Mevcut, A, B, C × 4 ekran (masaüstü 1440×900 ve telefon 375 px).
- `mevcut/`, `A/`, `B/`, `C/`: site ana sayfa üst bölümü, fiyatlar, panel Bugün, online randevu; masaüstü ve telefon.
- `kaynak/tema-*.css`: önizleme için token üstüne yazan CSS. Üretim: `cd frontend; npx playwright test -c playwright.demo.config.ts renk-onizleme`.
- Not: site giriş bölümündeki laptop/telefon içindeki ekran görüntüleri sabit resim; seçilen temayla `npm run demo:shots` yeniden üretilir.

Kontrast: bütün yazı/zemin çiftleri WCAG AA (4,5:1) üstünde. Neon, ışıma, cam efekti yok; sadece çok hafif gölge ve ince ton geçişi.

## A · Gece mavisi + altın
Koyu bantlar kalır ama daha dolgun bir lacivert; ana düğmeler sıcak altın, koyu zeminde küçük vurgular açık altın. Güven veren,
"bankacılık / premium üyelik" hissi (Revolut Metal, Amex, Monzo Plus tarzı lacivert + altın). En az değişiklikle en çok sıcaklık.
Marka mavisi logoda, Bugün şeritlerinde, ikon ve grafiklerde.

| Rol | Renk |
|---|---|
| Koyu yüzey | `#0c1a33` (ton geçişi `#11234a` → `#0c1a33`) |
| Koyu kart / hover | `#1b2d50` |
| Ana düğme (yazı `#0c1a33`) | `#f2b544` |
| Koyu zeminde vurgu | `#ffd27a` |
| Sayfa zemini | `#fcfbf8` (fildişi), kenarlık `#ebe6dc` |
| Link | `#006ea6` · marka mavisi `#00b0ff` |

## B · Ferah beyaz + mercan
Koyu bantlar kalkar: üst menü, giriş bölümü ve sol menü beyaz / çok açık mavi ton geçişi; yazı koyu mürekkep. Ana düğme marka
mavisi, küçük vurgular mercan. Hafif, modern, "SaaS" hissi (Stripe, Linear açık tema, Notion tarzı). En canlı ve ferah olanı;
logo beyaz zeminde koyu yazılı sürüm ister.

| Rol | Renk |
|---|---|
| Yazı | `#0b1526` (ikincil `#4a5566`) |
| Giriş bölümü ton geçişi | `#eaf6ff` → `#ffffff` |
| Ana düğme (yazı `#0b1526`) | `#00b0ff` |
| Vurgu zemini / vurgu yazısı | `#ff6a4d` / `#c2412d` |
| Sol menü seçili | zemin `#e5f6ff`, yazı `#005f8f` |
| Kenarlık / açık yüzey | `#e6ebf1` / `#f6f9fc` |

## C · Krem + antrasit
Soğuk lacivert yerine sıcak antrasit, beyaz yerine krem zemin; yazı antrasit, ana düğme marka mavisi. Sakin, sıcak, "butik /
kâğıt" hissi (Basecamp, Claude, Aesop tarzı krem tonları). Marka mavisi krem üstünde daha canlı görünür.

| Rol | Renk |
|---|---|
| Sayfa zemini / kart | `#f8f5ef` / `#fffdf9` |
| Yazı / ikincil yazı | `#1d1f23` / `#5b5750` |
| Koyu yüzey / hover | `#23262b` / `#33373e` |
| Ana düğme (yazı `#1d1f23`) | `#00b0ff` |
| Koyu zeminde vurgu | `#7fd6ff` |
| Kenarlık | `#e6e0d5` · link `#006ea6` |
