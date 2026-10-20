/**
 * Paketler ve fiyatlar — tanıtım sitesi bu dosyadan okur (karar 2026-10-17). Limitler ve fiyatlar veritabanında da var (Plans;
 * süper admin değiştirir); burası sitenin metni. İkisi değişince birlikte güncelleyin.
 *
 * Fiyatlar TL, KDV hariç. Yıllıkta 2 ay hediye. Pilot döneminde ücret alınmaz; online ödeme şirket kurulunca.
 */
export type PlanId = "cirak" | "kalfa" | "usta";

export type Plan = {
  id: PlanId;
  name: string;
  tagline: string;
  /** Aylık ödemede aylık TL; 0 → "Ücretsiz". */
  monthlyPrice: number;
  /** Yıllık ödemede yıllık TL. */
  yearlyPrice: number;
  /** Kullanıcı, müşteri ve aylık randevu sınırı (null = sınırsız). */
  limits: { users: number; contacts: number | null; appointments: number | null };
  features: string[];
  /** Öne çıkan paket (vurgulu kart). */
  highlighted?: boolean;
  cta: string;
};

export const plans: Plan[] = [
  {
    id: "cirak",
    name: "Çırak",
    tagline: "Tek başına çalışan, yeni başlayan esnaf için.",
    monthlyPrice: 0,
    yearlyPrice: 0,
    limits: { users: 1, contacts: 100, appointments: 50 },
    features: ["Müşteri kartları ve veresiye defteri", "Satış, kasa ve stok", "Randevu takvimi", "Raporlar ve Excel"],
    cta: "Ücretsiz başla",
  },
  {
    id: "kalfa",
    name: "Kalfa",
    tagline: "Ekibiyle çalışan, müşterisini büyütmek isteyen işletme için.",
    monthlyPrice: 699,
    yearlyPrice: 6990,
    limits: { users: 3, contacts: 1000, appointments: 500 },
    features: ["Çırak'taki her şey", "Online randevu sayfası", "Kampanya ve geri kazanma mesajı", "Otomatik hatırlatma ve doğum günü"],
    highlighted: true,
    cta: "14 gün ücretsiz dene",
  },
  {
    id: "usta",
    name: "Usta",
    tagline: "Büyüyen, personeli ve primi olan işletme için.",
    monthlyPrice: 1299,
    yearlyPrice: 12990,
    limits: { users: 10, contacts: null, appointments: null },
    features: ["Kalfa'daki her şey", "Personel primi", "Memnuniyet anketi ve Google yorum", "Kaybedilme riski listesi"],
    cta: "14 gün ücretsiz dene",
  },
];

export type Period = "aylik" | "yillik";

/** Bütün paketlerde ortak olanlar (paket kartlarının altında). */
export const allPlansInclude = [
  "Ücretsiz mali müşavir kullanıcısı (kullanıcı sayısına girmez)",
  "Panelden WhatsApp destek",
  "Mesai, izin ve personel kartları",
  "Paket bitse de verin silinmez, Excel'e aktarırsın",
];

export const planFootnote =
  "Fiyatlar TL ve KDV hariçtir; komisyon yoktur. Yıllık ödemede 2 ay hediye. Pilot döneminde ücret alınmaz; online ödeme yakında.";

const tl = (n: number) => `${n.toLocaleString("tr-TR")} TL`;

/** "Ücretsiz", "699 TL" (aylık) veya yıllıkta ayda düşen tutar "583 TL". */
export function formatPlanPrice(plan: Plan, period: Period = "aylik"): string {
  if (plan.monthlyPrice === 0) return "Ücretsiz";
  return tl(period === "yillik" ? Math.round(plan.yearlyPrice / 12) : plan.monthlyPrice);
}

/** Fiyatın altındaki not: "Yılda 6.990 TL ödenir" / "Aylık ödenir, istediğin ay bırak" / "Süresiz ücretsiz". */
export function planPriceNote(plan: Plan, period: Period): string {
  if (plan.monthlyPrice === 0) return "Süresiz ücretsiz";
  return period === "yillik" ? `Yılda ${tl(plan.yearlyPrice)} (2 ay hediye)` : "Aylık ödenir, istediğin ay bırakırsın";
}

/** "1 kullanıcı · 100 müşteri · ayda 50 randevu" */
export function planLimits(plan: Plan): string {
  const { users, contacts, appointments } = plan.limits;
  return [
    `${users} kullanıcı`,
    contacts === null ? "sınırsız müşteri" : `${contacts.toLocaleString("tr-TR")} müşteri`,
    appointments === null ? "sınırsız randevu" : `ayda ${appointments.toLocaleString("tr-TR")} randevu`,
  ].join(" · ");
}

/** Kayıt linki: /kayit?paket=kalfa&donem=yillik (Çırak'ta dönem yok). */
export function signupHref(plan: Plan, period: Period): string {
  return plan.monthlyPrice === 0 ? `/kayit?paket=${plan.id}` : `/kayit?paket=${plan.id}&donem=${period}`;
}
