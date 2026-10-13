import { istanbulParts, istanbulToUtcIso } from "@/lib/format";

/** Takvim yardımcıları: saatler İstanbul yerel saatinin gün içi dakikası (0 = 00:00, 1440 = gün sonu). */

export const DAY_MINUTES = 1440;

/** "14:30" → 870. */
export function parseHhmm(value: string): number {
  const [h, m] = value.split(":").map(Number);
  return h * 60 + m;
}

/** 870 → "14:30". 1440 → "24:00". */
export function hhmm(minutes: number): string {
  return `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
}

/** UTC anının İstanbul günü ("2026-10-14"). */
export function dayOf(iso: string): string {
  return istanbulParts(iso).date;
}

/** UTC anının İstanbul'daki gün içi dakikası. */
export function minutesOf(iso: string): number {
  return parseHhmm(istanbulParts(iso).time);
}

/** İstanbul gün + dakika → UTC ISO. */
export function toIso(date: string, minutes: number): string {
  return istanbulToUtcIso(date, hhmm(minutes));
}

/** En yakın adıma yuvarlar. */
export function snap(minutes: number, step: number): number {
  return Math.round(minutes / step) * step;
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

const dayFormat = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", weekday: "short", day: "numeric", month: "short" });
const longDayFormat = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", weekday: "long", day: "numeric", month: "long" });

/** "2026-10-14" → "Çar 14 Eki". */
export function dayLabel(date: string): string {
  return dayFormat.format(new Date(`${date}T00:00:00Z`));
}

/** "2026-10-14" → "14 Ekim Çarşamba". */
export function longDayLabel(date: string): string {
  return longDayFormat.format(new Date(`${date}T00:00:00Z`));
}

export type Block = { id: string; start: number; end: number };
export type PlacedBlock<T> = T & { lane: number; lanes: number };

/**
 * Üst üste binen blokları yan yana şeritlere yerleştirir (ör. iptal edilen randevular gösterilirken).
 * Birbirine değen bloklar bir küme oluşturur; kümedeki herkes kümenin şerit sayısı kadar daralır.
 */
export function layoutBlocks<T extends Block>(blocks: readonly T[]): PlacedBlock<T>[] {
  const sorted = [...blocks].sort((a, b) => a.start - b.start || b.end - a.end);
  const result: PlacedBlock<T>[] = [];
  let cluster: PlacedBlock<T>[] = [];
  let laneEnds: number[] = [];
  let clusterEnd = -1;

  const flush = () => {
    for (const item of cluster) item.lanes = laneEnds.length;
    result.push(...cluster);
    cluster = [];
    laneEnds = [];
  };

  for (const block of sorted) {
    if (block.start >= clusterEnd) flush();
    let lane = laneEnds.findIndex((end) => end <= block.start);
    if (lane === -1) {
      lane = laneEnds.length;
      laneEnds.push(block.end);
    } else {
      laneEnds[lane] = block.end;
    }
    cluster.push({ ...block, lane, lanes: 1 });
    clusterEnd = Math.max(clusterEnd, block.end);
  }
  flush();
  return result;
}

/**
 * Izgaranın gösterilecek saat aralığı: açık günlerin en erken açılışı ile en geç kapanışı ve bu aralığın dışına taşan
 * randevular; tam saate yuvarlanır. Hiç açık gün yoksa 09:00-19:00.
 */
export function gridBounds(
  days: readonly { opens?: string | null; closes?: string | null }[],
  blocks: readonly { start: number; end: number }[] = [],
): { start: number; end: number } {
  const opens = days.flatMap((d) => (d.opens ? [parseHhmm(d.opens)] : []));
  const closes = days.flatMap((d) => (d.closes ? [parseHhmm(d.closes)] : []));
  let start = opens.length ? Math.min(...opens) : 9 * 60;
  let end = closes.length ? Math.max(...closes) : 19 * 60;
  for (const b of blocks) {
    start = Math.min(start, b.start);
    end = Math.max(end, b.end);
  }
  return { start: Math.floor(start / 60) * 60, end: Math.min(DAY_MINUTES, Math.ceil(end / 60) * 60) };
}
