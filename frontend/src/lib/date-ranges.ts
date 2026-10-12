import { todayIstanbul } from "@/lib/format";

/** Rapor ve hesap hareketleri için tarih aralığı ön ayarları (İstanbul günü; hafta Pazartesi başlar). */
export type RangePreset = "bugun" | "bu-hafta" | "bu-ay" | "gecen-ay" | "bu-yil" | "ozel";

export const rangePresets: { key: RangePreset; label: string }[] = [
  { key: "bugun", label: "Bugün" },
  { key: "bu-hafta", label: "Bu hafta" },
  { key: "bu-ay", label: "Bu ay" },
  { key: "gecen-ay", label: "Geçen ay" },
  { key: "bu-yil", label: "Bu yıl" },
  { key: "ozel", label: "Özel aralık" },
];

export type DateRange = { from: string; to: string };

const iso = (d: Date) => d.toISOString().slice(0, 10);
const utc = (day: string) => new Date(`${day}T00:00:00Z`);

/** "2026-10-14" → aynı haftanın Pazartesi'si. */
export function startOfWeek(day: string): string {
  const d = utc(day);
  d.setUTCDate(d.getUTCDate() - ((d.getUTCDay() + 6) % 7));
  return iso(d);
}

export function addDays(day: string, days: number): string {
  const d = utc(day);
  d.setUTCDate(d.getUTCDate() + days);
  return iso(d);
}

/** Ön ayarın aralığı; "ozel" için verilen aralık (eksikse bu ay). Bitiş en fazla bugündür (bu hafta/ay/yıl bugüne kadar). */
export function presetRange(preset: RangePreset, today = todayIstanbul(), custom?: Partial<DateRange>): DateRange {
  const monthStart = `${today.slice(0, 8)}01`;
  switch (preset) {
    case "bugun":
      return { from: today, to: today };
    case "bu-hafta":
      return { from: startOfWeek(today), to: today };
    case "gecen-ay": {
      const lastOfPrev = addDays(monthStart, -1);
      return { from: `${lastOfPrev.slice(0, 8)}01`, to: lastOfPrev };
    }
    case "bu-yil":
      return { from: `${today.slice(0, 4)}-01-01`, to: today };
    case "ozel": {
      const from = custom?.from || monthStart;
      const to = custom?.to || today;
      return from <= to ? { from, to } : { from: to, to: from };
    }
    default:
      return { from: monthStart, to: today };
  }
}

export function isPreset(value: string | null | undefined): value is RangePreset {
  return rangePresets.some((p) => p.key === value);
}
