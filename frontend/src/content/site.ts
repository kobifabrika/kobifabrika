/** Tanıtım sitesi metinleri ve genel bilgileri. Şirket bilgisi content/company.ts'ten, paketler content/plans.ts'ten. */
import type { SectorIconName } from "@/components/site/sector-icons";

import { company } from "./company";

/** Slogan: sayfa başlığı, meta açıklama, site ve giriş ekranları (karar 2026-10-15; eski "işletim sistemi" ifadesi kullanılmaz). */
export const siteSlogan = "KOBİ'nin dijital fabrikası";

export const siteDescription =
  "Esnafın kasası, veresiyesi, randevusu ve personeli tek uygulamada. Telefondan kullanılır, 10 dakikada kurulur, Çırak paketi ücretsiz.";

export type SocialKey = "instagram" | "facebook" | "youtube" | "tiktok";

/** Sosyal medya hesapları (site alt bilgisinde ikonlarıyla). */
export const siteSocial: readonly { key: SocialKey; label: string; href: string }[] = [
  { key: "instagram", label: "Instagram", href: "https://www.instagram.com/kobifabrika" },
  { key: "facebook", label: "Facebook", href: "https://www.facebook.com/kobifabrika" },
  { key: "youtube", label: "YouTube", href: "https://www.youtube.com/@kobifabrika" },
  { key: "tiktok", label: "TikTok", href: "https://www.tiktok.com/@kobifabrika" },
];

export const siteContact = {
  company: company.name,
  email: company.email,
};

/** Üst menü. */
export const siteNav = [
  { href: "/#ozellikler", label: "Özellikler" },
  { href: "/fiyatlar", label: "Fiyatlar" },
  { href: "/sss", label: "SSS" },
  { href: "/yardim", label: "Yardım" },
  { href: "/hakkimizda", label: "Hakkımızda" },
];

/** Alt bilgideki site bağlantıları. */
export const footerNav = [...siteNav, { href: "/iletisim", label: "İletişim" }];

/** Ana çağrı (her yerde aynı metin). */
export const primaryCta = { href: "/kayit", label: "Ücretsiz dene" };

export const sectors: { icon: SectorIconName; title: string; text: string }[] = [
  { icon: "kuafor", title: "Kuaför ve berber", text: "Randevu takvimi, online randevu linki, gelmeyen müşteriye hatırlatma." },
  { icon: "guzellik", title: "Güzellik ve bakım", text: "Paket ve seans takibi, personel primi, memnuniyet anketi." },
  { icon: "studyo", title: "Pilates ve spor stüdyosu", text: "Seans paketleri, ders saatleri, üyenin borcu ve ödemesi aynı kartta." },
  { icon: "kafe", title: "Kafe ve restoran", text: "Hızlı satış, veresiye ve cari, kasa ve gider, personel mesaisi." },
  { icon: "dukkan", title: "Dükkân ve mağaza", text: "Barkodla satış, stok ve kritik stok uyarısı, tedarikçi cari hesabı." },
];

export type FeatureGroup = { key: string; label: string; title: string; text: string; points: string[]; shot: string; shotAlt: string };

/** Özellikler: para, müşteri, ekip. Görseller demo işletmenin gerçek ekranları (npm run demo:shots). */
export const featureGroups: FeatureGroup[] = [
  {
    key: "para",
    label: "Para",
    title: "Veresiye defterini kapat.",
    text: "Kim ne aldı, ne ödedi, ne kadar borcu kaldı; her müşterinin hesabı ayrı. Vadesi gelene WhatsApp'tan tek dokunuşla hatırlat.",
    points: ["Kasa, banka, gelir ve gider", "Veresiye, vade ve tahsilat linki", "Satış, teklif, stok ve fiş", "Mali müşavire Excel"],
    shot: "/site/cari-masaustu.webp",
    shotAlt: "KobiFabrika cari ve kasa ekranı: müşterilerin bakiyeleri, vadesi gelen alacaklar ve hatırlat düğmesi.",
  },
  {
    key: "musteri",
    label: "Müşteri",
    title: "Boş koltuk kalmasın.",
    text: "Müşteri randevusunu senin linkinden kendisi alır, sen onaylarsın. Yarının randevularını tek tek WhatsApp'tan hatırlatırsın; gelmeyeni geri çağırırsın.",
    points: ["Personel bazında takvim", "Online randevu linki", "Yarının randevuları ve hatırlatma", "Gelmeyen müşteri ve kaybedilme riski"],
    shot: "/site/takvim-masaustu.webp",
    shotAlt: "KobiFabrika randevu takvimi: personel kolonlarında günün randevuları.",
  },
  {
    key: "ekip",
    label: "Ekip",
    title: "Ay sonu hesabıyla uğraşma.",
    text: "Mesai giriş-çıkışı ve izin talebi telefondan. Satıştan prim kendiliğinden hesaplanır; maaşı sadece işletme sahibi görür.",
    points: ["Mesai ve fazla mesai", "İzin talebi, onay ve bakiye", "Satıştan prim", "Rol bazlı yetki"],
    shot: "/site/personel-masaustu.webp",
    shotAlt: "KobiFabrika personel ekranı: personel listesi, mesai durumu ve prim.",
  },
];

export const differentiators = [
  { title: "Tek uygulama, iki iş", text: "Para takibi ve müşteri kazanma aynı yerde; iki program, iki abonelik yok." },
  { title: "Telefonda çalışır", text: "Uygulama indirmeden tarayıcıdan açılır; ana ekrana eklersin." },
  { title: "TL, sabit fiyat", text: "Fiyat TL; kur farkı yok, satışından komisyon almayız." },
  { title: "Veri senin", text: "Paket bitse de verin silinmez; istediğin zaman Excel'e aktarırsın." },
  { title: "Aracısız destek", text: "Panelden WhatsApp ile doğrudan ekibe yazarsın; bayi ve çağrı merkezi yok." },
  { title: "Mali müşavir ücretsiz", text: "Mali müşavirin salt okunur kullanıcı olarak girer, kullanıcı sayına girmez." },
];

export const steps = [
  { title: "Üye ol", text: "Ad, e-posta ve telefonla bir dakikada hesabını aç." },
  { title: "İşletmeni kur", text: "Sektörünü ve çalışma saatlerini seç; menü ve hizmet listesi ona göre gelir." },
  { title: "Kullanmaya başla", text: "İlk satışını, randevunu ya da veresiyeni gir. Müşteri listeni Excel'den aktar." },
];

export const faqs: { q: string; a: string }[] = [
  {
    q: "Ücretsiz paket gerçekten ücretsiz mi?",
    a: "Evet. Çırak paketi süresiz ücretsizdir; kredi kartı istenmez. Kalfa ve Usta'yı 14 gün bütün özellikleriyle ücretsiz denersiniz; deneme bitince paket seçmezseniz Çırak'a geçersiniz, verileriniz silinmez.",
  },
  {
    q: "Pilot dönemi ne demek?",
    a: "Şu anda seçili işletmelerle pilot dönemindeyiz: pilot işletmeler bütün özellikleri süresiz ve ücretsiz kullanır, karşılığında bize görüşlerini anlatır. Ücretli paketlere geçiş başlamadan en az 30 gün önce haber veririz.",
  },
  {
    q: "Telefondan çalışır mı?",
    a: "Evet. KobiFabrika önce telefon için tasarlandı; uygulama indirmeden tarayıcıdan açılır, ana ekrana ekleyip uygulama gibi kullanırsınız. Bilgisayarda ve tablette de aynı hesapla çalışır.",
  },
  {
    q: "Müşterilerime randevu hatırlatması nasıl gider?",
    a: "Bugün ekranındaki \"Yarının randevuları\" listesinden her müşteriye WhatsApp'tan hazır mesajla hatırlatırsınız; müşterinin e-postası varsa hatırlatma e-postası kendiliğinden gider. SMS ile otomatik hatırlatma yakında.",
  },
  {
    q: "Verilerim güvende mi?",
    a: "Bağlantılar şifrelidir, her işletmenin verisi diğerlerinden ayrı tutulur ve her gece şifreli yedek alınır. Verileriniz satılmaz, reklam için kullanılmaz; destek ekibimiz müşteri verilerinizi göremez.",
  },
  {
    q: "Mali müşavirim sisteme girebilir mi?",
    a: "Evet, ücretsiz. Mali müşaviriniz salt okunur kullanıcı olarak girer, raporları ve hareketleri Excel olarak indirir. Kullanıcı sayınıza dahil edilmez.",
  },
  {
    q: "Mevcut müşteri ve ürün listemi aktarabilir miyim?",
    a: "Evet. Excel şablonunu indirip doldurun, müşteri ve ürünleriniz birkaç dakikada içeri aktarılır.",
  },
  {
    q: "e-Fatura ve e-Arşiv var mı?",
    a: "Henüz yok; sonraki aşamada geliyor. Şimdilik satış ve teklif belgelerini PDF olarak müşterinize gönderebilirsiniz.",
  },
  {
    q: "Destek nasıl alırım?",
    a: "Panelden WhatsApp ile doğrudan KobiFabrika ekibine yazarsınız; bayi veya çağrı merkezi yoktur. Sık sorulanlar için yardım makaleleri de var.",
  },
];
