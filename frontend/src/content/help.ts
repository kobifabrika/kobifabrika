import { primaryCta } from "./site";
import { isSiteOnly } from "./site-mode";

/** İlk adım düğmenin arayüzdeki adıyla (primaryCta); sadece-site modunda kayıt yok, pilot listesine WhatsApp'tan katılınır. */
const firstStep = isSiteOnly()
  ? {
      title: "Pilot listesine katıl",
      text: `kobifabrika.com'da "${primaryCta.label}" düğmesine dokun; WhatsApp'ta hazır gelen mesaja işletmeni ekleyip gönder. Panel açılınca ilk sana haber veririz, sonraki adımlar o zaman.`,
    }
  : {
      title: "Üye ol",
      text: `kobifabrika.com'da "${primaryCta.label}" düğmesine dokun; adını, e-postanı, cep telefonunu ve şifreni yaz.`,
    };

/**
 * Yardım merkezi makaleleri (/yardim). Görseller demo işletmenin telefondaki ekranları (`npm run demo:shots` → public/yardim).
 * İlk etapta 4 makale (karar 2026-10-17); kalanlar yayından sonra (docs/05 → 8A Yayından sonra).
 */
export type HelpArticle = {
  slug: string;
  title: string;
  summary: string;
  minutes: number;
  image: { src: string; alt: string };
  steps: { title: string; text: string }[];
  tips?: string[];
};

export const helpArticles: HelpArticle[] = [
  {
    slug: "ilk-kurulum",
    title: "İlk kurulum: 10 dakikada başla",
    summary: "Hesabını aç, işletmeni tanıt, çalışma saatlerini ve hizmetlerini gir.",
    minutes: 4,
    image: {
      src: "/yardim/ilk-kurulum.webp",
      alt: "Telefonda kurulum sihirbazının ilk adımı: işletme adı, sektör ve telefon alanları.",
    },
    steps: [
      firstStep,
      {
        title: "E-postanı doğrula",
        text: "Gelen \"E-posta adresini doğrula\" e-postasındaki bağlantıya dokun. Gelmezse İstenmeyen klasörüne bak; panelden tekrar gönderebilirsin.",
      },
      {
        title: "İşletmeni tanıt",
        text: "Kurulum sihirbazında işletme adını, sektörünü (kuaför, kafe, stüdyo…) ve telefonunu gir. Sektöre göre menü ve hizmet listesi hazır gelir.",
      },
      { title: "Çalışma saatlerini seç", text: "Açılış-kapanış saatlerini ve kapalı günleri işaretle. Randevu takvimi bu saatlere göre açılır." },
      {
        title: "Hizmetlerini ve personelini ekle",
        text: "Ürünler/Hizmetler'den hizmetin adını, süresini ve fiyatını gir. Personel'den çalışanlarını ekle; her birinin hangi hizmeti verdiğini seç.",
      },
      { title: "Müşterilerini aktar", text: "Müşteriler → İçe aktar ile Excel şablonunu indir, doldur ve yükle. Birkaç dakikada liste hazır." },
    ],
    tips: ["Mali müşavirini Ayarlar → Ekip'ten ücretsiz davet edebilirsin; salt okunur girer.", "Takıldığın yerde menüdeki Yardım'dan WhatsApp ile bize yaz."],
  },
  {
    slug: "online-randevu-linki",
    title: "Online randevu linkini paylaşma",
    summary: "Müşterilerin randevusunu senin linkinden kendisi alsın; sen onayla.",
    minutes: 3,
    image: {
      src: "/yardim/online-randevu-linki.webp",
      alt: "Telefonda Ayarlar → Randevu ekranı: online randevu sayfası açık, randevu linki ve Kopyala düğmesi.",
    },
    steps: [
      { title: "Sayfayı aç", text: "Ayarlar → Randevu → Online randevu'da \"Online randevu sayfası açık\" anahtarını aç ve Kaydet'e dokun." },
      {
        title: "Personel ve hizmet görünsün",
        text: "Online sayfada görünmesini istediğin personelde \"Online randevuda görünsün\" açık olmalı; hizmetlerin süresi tanımlı olmalı.",
      },
      { title: "Linki kopyala", text: "\"Randevu linkiniz\" altındaki Kopyala düğmesine dokun. Önizle ile müşterinin göreceği sayfayı aç." },
      {
        title: "Paylaş",
        text: "Linki Instagram profiline (bio), Google işletme kaydına, WhatsApp durumuna ve işletme kartvizitine ekle.",
      },
      {
        title: "Talepleri onayla",
        text: "Müşteri randevu talebi gönderince sana bildirim gelir; takvimde \"Beklemede\" görünür. Randevuya dokun, Onayla'ya bas ve müşteriye WhatsApp'tan haber ver.",
      },
    ],
    tips: [
      "Müşteri e-postasını yazdıysa onay e-postası kendiliğinden gider.",
      "Aynı numarayla en fazla iki bekleyen talep alınır; sahte taleplere karşı bot koruması vardır.",
    ],
  },
  {
    slug: "whatsapp-hatirlatma",
    title: "Yarının randevularını WhatsApp'tan hatırlatma",
    summary: "Gelmeyen müşteriyi azalt: her akşam tek dokunuşla hatırlatma gönder.",
    minutes: 2,
    image: {
      src: "/yardim/whatsapp-hatirlatma.webp",
      alt: "Telefonda Bugün ekranında Yarının randevuları kartı: her randevunun yanında WhatsApp düğmesi.",
    },
    steps: [
      { title: "Bugün ekranını aç", text: "\"Yarının randevuları\" kartında yarın gelecek müşteriler saat sırasıyla listelenir." },
      {
        title: "WhatsApp'a dokun",
        text: "Müşterinin satırındaki WhatsApp düğmesine dokun. WhatsApp hazır mesajla açılır: işletmen, gün, saat, hizmet ve iptal linki.",
      },
      { title: "Gönder", text: "WhatsApp'ta Gönder'e bas ve KobiFabrika'ya dön. Satırda \"Hatırlatıldı\" ve saati görünür." },
      { title: "Sıradakine geç", text: "Kartın üstündeki \"Hatırlatılan 3/8\" sayacı kaç kişinin kaldığını gösterir." },
    ],
    tips: [
      "Aynı liste takvimde yarının gününe geçince \"Yarının hatırlatmaları\" şeridinde de var.",
      "Onay, saat değişikliği ve iptalde de randevu penceresindeki \"WhatsApp'tan bildir\" düğmesini kullan.",
    ],
  },
  {
    slug: "telefonda-kullanim",
    title: "Telefonda kullanım ve ana ekrana ekleme",
    summary: "KobiFabrika'yı uygulama gibi tek dokunuşla aç.",
    minutes: 2,
    image: {
      src: "/yardim/telefonda-kullanim.webp",
      alt: "Telefonda KobiFabrika menüsü: hesap, ana menü ve Yardım bölümü.",
    },
    steps: [
      { title: "Tarayıcıdan gir", text: "Telefonunun tarayıcısında kobifabrika.com'a girip giriş yap. Uygulama indirmen gerekmez." },
      { title: "iPhone'da ana ekrana ekle", text: "Safari'de alttaki Paylaş düğmesine, sonra \"Ana Ekrana Ekle\"ye dokun." },
      { title: "Android'de ana ekrana ekle", text: "Chrome'da sağ üstteki üç noktaya, sonra \"Ana ekrana ekle\" ya da \"Uygulamayı yükle\"ye dokun." },
      {
        title: "Alt çubuğu kullan",
        text: "Alttaki çubukta Bugün, Randevu, ortada hızlı işlem (+), Satış ve Müşteri var. Diğer her şey sağ üstteki Menü'de.",
      },
    ],
    tips: ["\"Beni hatırla\"yı işaretlersen 30 gün boyunca yeniden giriş yapman gerekmez."],
  },
];

export function findArticle(slug: string): HelpArticle | undefined {
  return helpArticles.find((a) => a.slug === slug);
}
