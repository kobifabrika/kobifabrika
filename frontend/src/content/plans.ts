/**
 * Paketler ve fiyatlar — tanıtım sitesi bu dosyadan okur; fiyat değişince sadece burayı güncelleyin.
 *
 * `monthlyPrice`: aylık TL, KDV hariç, yıllık ödemede. `null` → "Lansman fiyatı yakında" yazılır; `0` → "Ücretsiz".
 * Onaylı aralık (docs/00-urun-ozeti.md): Kalfa 599-799 TL, Usta 1.199-1.499 TL.
 *
 * TASLAK: paket içerikleri ve limitler Sprint 8'de (Abonelik) netleşecek; buradaki özellik listesi tanıtım içindir.
 */
export type Plan = {
  id: "cirak" | "kalfa" | "usta";
  name: string;
  tagline: string;
  monthlyPrice: number | null;
  /** Fiyatın altındaki kısa not. */
  priceNote: string;
  features: string[];
  /** Öne çıkan paket (vurgulu kart). */
  highlighted?: boolean;
  cta: string;
};

export const plans: Plan[] = [
  {
    id: "cirak",
    name: "Çırak",
    tagline: "Yeni başlayan ve tek başına çalışan esnaf için.",
    monthlyPrice: 0,
    priceNote: "Süresiz ücretsiz",
    features: [
      "1 kullanıcı + ücretsiz mali müşavir",
      "Müşteri kartları ve veresiye defteri",
      "Satış, kasa ve basit stok",
      "Online randevu sayfası",
      "Sınırlı müşteri ve aylık randevu sayısı",
    ],
    cta: "Ücretsiz başla",
  },
  {
    id: "kalfa",
    name: "Kalfa",
    tagline: "Parasını ve ekibini düzene sokmak isteyen işletme için.",
    monthlyPrice: null,
    priceNote: "Tek başına bir ön muhasebe programından ucuz",
    features: [
      "Çırak'taki her şey, daha geniş limitlerle",
      "Birden fazla kullanıcı ve rol",
      "Cari hesap, kasa ve tahsilat takibi",
      "Teklif ve satış belgesi (PDF)",
      "Stok takibi ve kritik stok uyarısı",
      "Randevu hatırlatma SMS'i",
    ],
    highlighted: true,
    cta: "14 gün ücretsiz dene",
  },
  {
    id: "usta",
    name: "Usta",
    tagline: "Büyümek, müşterisini geri getirmek isteyen işletme için.",
    monthlyPrice: null,
    priceNote: "Ön muhasebe + randevu programının toplamından ucuz",
    features: [
      "Kalfa'daki her şey",
      "SMS kampanyası ve geri kazanma mesajı",
      "Boş saat kampanyası",
      "Memnuniyet anketi ve Google yorum",
      "Personel, mesai, izin ve prim",
      "Tüm raporlar ve Excel dışa aktarma",
    ],
    cta: "14 gün ücretsiz dene",
  },
];

/** Bütün paketlerde ortak olanlar (paket kartlarının altında). */
export const allPlansInclude = [
  "Ücretsiz mali müşavir kullanıcısı",
  "Panelden WhatsApp destek",
  "Yenilemeden 7 gün önce hatırlatma, tek tıkla iptal",
  "Abonelik bitse de salt okunur erişim ve Excel dışa aktarma",
];

export const planFootnote = "Fiyatlar TL, KDV hariç ve komisyonsuzdur. SMS kredisi ayrı satılır.";

export function formatPlanPrice(plan: Plan): string {
  if (plan.monthlyPrice === null) return "Lansman fiyatı yakında";
  if (plan.monthlyPrice === 0) return "Ücretsiz";
  return `${plan.monthlyPrice.toLocaleString("tr-TR")} TL`;
}
