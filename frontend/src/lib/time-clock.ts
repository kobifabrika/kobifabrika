/** Mesai ve izin ekranlarının saf yardımcıları (Vitest; backend'deki WorkTimeCalculator ve LeaveTexts ile aynı kurallar). */

/** 192 → "3 sa 12 dk", 45 → "45 dk", 120 → "2 sa", 0 → "0 dk". */
export function formatDuration(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  const hours = Math.floor(m / 60);
  const rest = m % 60;
  if (hours === 0) return `${rest} dk`;
  return rest === 0 ? `${hours} sa` : `${hours} sa ${rest} dk`;
}

/** Rapor sütunu: 2730 → "45,5"; 2700 → "45" (saat, en fazla 2 ondalık, Türkçe virgül). */
export function hoursText(minutes: number): string {
  return (Math.round((minutes / 60) * 100) / 100).toLocaleString("tr-TR", { maximumFractionDigits: 2 });
}

/** Açık mesainin şu ana kadarki süresi (dakika). */
export function elapsedMinutes(fromIso: string, nowMs: number): number {
  return Math.max(0, Math.floor((nowMs - new Date(fromIso).getTime()) / 60_000));
}

/** Haftalık eşiğe göre fazla mesai (dakika). */
export function overtimeMinutes(weekMinutes: number, thresholdHours: number): number {
  return Math.max(0, weekMinutes - Math.round(thresholdHours * 60));
}

/** 1 → "1 gün", 0.5 → "Yarım gün", 3.5 → "3,5 gün", -2 → "-2 gün". */
export function daysText(days: number): string {
  if (days === 0.5) return "Yarım gün";
  return `${days.toLocaleString("tr-TR", { maximumFractionDigits: 1 })} gün`;
}

const longDate = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", day: "numeric", month: "long", year: "numeric" });
const dayMonth = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", day: "numeric", month: "long" });

/** "2026-11-03", "2026-11-05" → "3 – 5 Kasım 2026"; aynı gün → "3 Kasım 2026"; farklı yıl → "30 Aralık 2026 – 2 Ocak 2027". */
export function dateRangeText(start: string, end: string): string {
  const s = new Date(`${start}T00:00:00Z`);
  const e = new Date(`${end}T00:00:00Z`);
  if (start === end) return longDate.format(s);
  if (s.getUTCFullYear() !== e.getUTCFullYear()) return `${longDate.format(s)} – ${longDate.format(e)}`;
  if (s.getUTCMonth() === e.getUTCMonth()) return `${s.getUTCDate()} – ${longDate.format(e)}`;
  return `${dayMonth.format(s)} – ${longDate.format(e)}`;
}

/** Ayın günleri: "2026-11" → ["2026-11-01", …, "2026-11-30"]. */
export function monthDays(month: string): string[] {
  const [y, m] = month.split("-").map(Number) as [number, number];
  const count = new Date(Date.UTC(y, m, 0)).getUTCDate();
  return Array.from({ length: count }, (_, i) => `${month}-${String(i + 1).padStart(2, "0")}`);
}

/** "2026-11" → "2026-12" (delta = 1) veya "2026-10" (delta = -1). */
export function addMonths(month: string, delta: number): string {
  const [y, m] = month.split("-").map(Number) as [number, number];
  const d = new Date(Date.UTC(y, m - 1 + delta, 1));
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}`;
}

const monthFormat = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", month: "long", year: "numeric" });

/** "2026-11" → "Kasım 2026". */
export function monthLabel(month: string): string {
  return monthFormat.format(new Date(`${month}-01T00:00:00Z`));
}

/** Gün aralığın içinde mi (uçlar dahil, "yyyy-MM-dd" metin karşılaştırması). */
export function inRange(day: string, start: string, end: string): boolean {
  return day >= start && day <= end;
}
