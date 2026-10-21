/** Tanıtım sitesi metinleri ve genel bilgileri. Şirket bilgisi content/company.ts'ten, paketler content/plans.ts'ten. */
import { company } from "./company";

/** Slogan: sayfa başlığı, meta açıklama, site ve giriş ekranları (karar 2026-10-15; eski "işletim sistemi" ifadesi kullanılmaz). */
export const siteSlogan = "KOBİ'nin dijital fabrikası";

/** Ana sayfa sekme ve Open Graph başlığı. */
export const homeTitle = `KobiFabrika | ${siteSlogan}`;

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
  { href: "/ozellikler", label: "Özellikler" },
  { href: "/fiyatlar", label: "Fiyatlar" },
  { href: "/sss", label: "SSS" },
  { href: "/yardim", label: "Yardım" },
  { href: "/hakkimizda", label: "Hakkımızda" },
];

/** Alt bilgideki site bağlantıları. */
export const footerNav = [...siteNav, { href: "/iletisim", label: "İletişim" }];

/** Alt bilgi kolonları (hukuki metinler `legalLinks` ile ayrıca). */
export const footerColumns: { title: string; links: { href: string; label: string }[] }[] = [
  {
    title: "Ürün",
    links: [
      { href: "/ozellikler", label: "Özellikler" },
      { href: "/#kimler", label: "Kimler için" },
      { href: "/fiyatlar", label: "Fiyatlar" },
      { href: "/kayit", label: "Ücretsiz dene" },
    ],
  },
  {
    title: "Destek",
    links: [
      { href: "/yardim", label: "Yardım makaleleri" },
      { href: "/sss", label: "Sık sorulan sorular" },
      { href: "/iletisim", label: "İletişim ve WhatsApp" },
    ],
  },
  {
    title: "Şirket",
    links: [
      { href: "/hakkimizda", label: "Hakkımızda" },
      { href: "/giris", label: "Giriş yap" },
    ],
  },
];

/** Ana çağrı (her yerde aynı metin). */
export const primaryCta = { href: "/kayit", label: "Ücretsiz dene" };

/**
 * Kimler için: sektör kartları. Fotoğraflar sadece atmosfer (Pexels, docs/foto-kaynaklari.md); kişilere isim veya yorum yazılmaz,
 * alt metin sahneyi anlatır.
 */
export const sectors: { photo: string; alt: string; title: string; text: string }[] = [
  {
    photo: "/site/foto/berber.webp",
    alt: "Berberde makasla saçı kesilen sakallı müşteri.",
    title: "Kuaför ve berber",
    text: "Randevu takvimi, online randevu linki, gelmeyen müşteriye hatırlatma.",
  },
  {
    photo: "/site/foto/guzellik.webp",
    alt: "Güzellik salonunda müşterisine makyaj yapmaya hazırlanan makyöz.",
    title: "Güzellik ve bakım",
    text: "Paket ve seans takibi, personel primi, memnuniyet anketi.",
  },
  {
    photo: "/site/foto/pilates.webp",
    alt: "Pilates stüdyosunda reformer aletinde çalışan üyesine yardım eden eğitmen.",
    title: "Pilates ve spor stüdyosu",
    text: "Seans paketleri, ders saatleri, üyenin borcu ve ödemesi aynı kartta.",
  },
  {
    photo: "/site/foto/kafe.webp",
    alt: "Kafenin tezgâhında elinde süt potu ve fincanla gülümseyen barista.",
    title: "Kafe ve restoran",
    text: "Hızlı satış, veresiye ve cari, kasa ve gider, personel mesaisi.",
  },
  {
    photo: "/site/foto/cicekci.webp",
    alt: "Çiçekçi dükkânında tezgâhta buket hazırlayan kadın.",
    title: "Dükkân ve mağaza",
    text: "Barkodla satış, stok ve kritik stok uyarısı, tedarikçi cari hesabı.",
  },
];

export type FeatureGroup = {
  key: string;
  label: string;
  title: string;
  text: string;
  points: string[];
  shot: string;
  shotAlt: string;
};

/** Özellikler: para, müşteri, ekip. Görseller demo işletmenin gerçek ekranları (npm run demo:shots). */
export const featureGroups: FeatureGroup[] = [
  {
    key: "para",
    label: "Para",
    title: "Veresiye defterini kapat.",
    text: "Kim ne aldı, ne ödedi, ne kadar borcu kaldı; her müşterinin hesabı ayrı. Vadesi gelene WhatsApp'tan tek dokunuşla hatırlat.",
    points: [
      "Kasa, banka, gelir ve gider",
      "Veresiye, vade ve tahsilat linki",
      "Satış, teklif, stok ve fiş",
      "Mali müşavire Excel",
    ],
    shot: "/site/cari-masaustu.webp",
    shotAlt: "KobiFabrika cari ve kasa ekranı: müşterilerin bakiyeleri, vadesi gelen alacaklar ve hatırlat düğmesi.",
  },
  {
    key: "musteri",
    label: "Müşteri",
    title: "Boş koltuk kalmasın.",
    text: "Müşteri randevusunu senin linkinden kendisi alır, sen onaylarsın. Yarının randevularını tek tek WhatsApp'tan hatırlatırsın; gelmeyeni geri çağırırsın.",
    points: [
      "Personel bazında takvim",
      "Online randevu linki",
      "Yarının randevuları ve hatırlatma",
      "Gelmeyen müşteri ve kaybedilme riski",
    ],
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

/**
 * Özellikler sayfası (/ozellikler, 2026-10-21): modül başına ayrıntılı liste. Sadece ürünün bugün yaptıkları; SMS pilotta kapalı
 * olduğu için SMS'e dayanan özellikler yazılmaz (FAQ'deki "SMS yakında" ile aynı dil).
 */
export const featureDetails: Record<string, { title: string; text: string }[]> = {
  para: [
    {
      title: "Satış ve fiş",
      text: "Ürün ve hizmeti seç, nakit, kart, havale ya da veresiye kaydet; stok ve kasa kendiliğinden güncellenir.",
    },
    {
      title: "Veresiye ve cari",
      text: "Her müşterinin ve tedarikçinin borcu, ödemesi ve ekstresi ayrı; ekstreyi linkle gönderirsin.",
    },
    {
      title: "Vade ve tahsilat",
      text: "Vadesi gelen ve geciken alacaklar tek listede; WhatsApp'tan hazır mesajla hatırlatırsın.",
    },
    {
      title: "Kasa, banka, gelir ve gider",
      text: "Nakit kasa ve banka hesapları, kira ve fatura gibi giderler; gün sonu ne kaldığını görürsün.",
    },
    { title: "Teklif", text: "Müşterine PDF teklif hazırla, onaylanınca tek dokunuşla satışa çevir." },
    { title: "Stok", text: "Barkodla ürün, stok hareketi, kritik stok uyarısı ve sayım." },
    {
      title: "Raporlar ve Excel",
      text: "Satış, tahsilat, gider ve personel raporları; mali müşavirine Excel olarak verirsin.",
    },
  ],
  musteri: [
    {
      title: "Randevu takvimi",
      text: "Personel kolonlarında gün ve hafta görünümü; bilgisayarda sürükleyip taşırsın, çakışmayı uygulama engeller.",
    },
    {
      title: "Online randevu linki",
      text: "Müşterin hizmeti ve saati kendisi seçer, sen onaylarsın. Linki Instagram ve Google kaydına koyarsın.",
    },
    {
      title: "Yarının randevuları",
      text: "Ertesi günün listesi Bugün ekranında; her müşteriye WhatsApp'tan tek dokunuşla hatırlatma.",
    },
    { title: "Müşteri kartı", text: "Geçmiş randevular, satışlar, borç, notlar ve iletişim izni tek kartta." },
    { title: "Paket ve seans", text: "10 seanslık paket sat, her gelişte seans düşer; kalan seans müşteri kartında." },
    {
      title: "Geri kazanma",
      text: "Uzun süredir gelmeyen ve kaybedilme riski olan müşteriler listelenir; geri çağırırsın.",
    },
    {
      title: "Memnuniyet anketi",
      text: "Hizmetten sonra kısa anket; memnun müşteriyi Google yorumuna yönlendirirsin.",
    },
  ],
  ekip: [
    { title: "Mesai", text: "Personel mesaiye telefondan başlar ve bitirir; fazla mesai raporda." },
    { title: "İzin", text: "İzin talebi, onay ve yıllık izin bakiyesi; onaylı izin takvimde görünür." },
    { title: "Prim", text: "Satıştan prim kendiliğinden hesaplanır; personel sadece kendi primini görür." },
    { title: "Rol ve yetki", text: "Sahip, Yönetici, Personel ve Mali Müşavir; maaşı sadece işletme sahibi görür." },
    { title: "Personel kartı", text: "Çalışma saatleri, verdiği hizmetler, maaş ve izin geçmişi tek kartta." },
  ],
};

export const differentiators = [
  { title: "Tek uygulama, iki iş", text: "Para takibi ve müşteri kazanma aynı yerde; iki program, iki abonelik yok." },
  { title: "Telefonda çalışır", text: "Uygulama indirmeden tarayıcıdan açılır; ana ekrana eklersin." },
  { title: "TL, sabit fiyat", text: "Fiyat TL; kur farkı yok, satışından komisyon almayız." },
  { title: "Veri senin", text: "Paket bitse de verin silinmez; istediğin zaman Excel'e aktarırsın." },
  { title: "Aracısız destek", text: "Panelden WhatsApp ile doğrudan ekibe yazarsın; bayi ve çağrı merkezi yok." },
  {
    title: "Mali müşavir ücretsiz",
    text: "Mali müşavirin salt okunur kullanıcı olarak girer, kullanıcı sayına girmez.",
  },
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
    a: 'Bugün ekranındaki "Yarının randevuları" listesinden her müşteriye WhatsApp\'tan hazır mesajla hatırlatırsınız; müşterinin e-postası varsa hatırlatma e-postası kendiliğinden gider. SMS ile otomatik hatırlatma yakında.',
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
