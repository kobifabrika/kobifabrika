import { addDays } from "@/lib/date-ranges";
import { istanbulToUtcIso } from "@/lib/format";

/** Online randevu adımları (?adim=). */
export const bookingSteps = ["hizmet", "personel", "saat", "bilgiler", "kod", "onay"] as const;
export type BookingStep = (typeof bookingSteps)[number];

export function isBookingStep(value: string | null): value is BookingStep {
  return !!value && (bookingSteps as readonly string[]).includes(value);
}

export type SlotGroup = { label: "Sabah" | "Öğle" | "Akşam"; times: string[] };

/** Boş saatleri sabah (12:00 öncesi), öğle (12:00-16:59) ve akşam (17:00 ve sonrası) gruplarına ayırır; boş grup çıkmaz. */
export function groupSlots(times: readonly string[]): SlotGroup[] {
  const groups: SlotGroup[] = [
    { label: "Sabah", times: [] },
    { label: "Öğle", times: [] },
    { label: "Akşam", times: [] },
  ];
  for (const t of times) {
    const hour = Number(t.slice(0, 2));
    groups[hour < 12 ? 0 : hour < 17 ? 1 : 2]!.times.push(t);
  }
  return groups.filter((g) => g.times.length > 0);
}

/** Gün şeridi: from'dan başlayan 7 gün ("YYYY-MM-DD"). */
export function weekFrom(from: string): string[] {
  return Array.from({ length: 7 }, (_, i) => addDays(from, i));
}

/** Şeridin ileri/geri gidebileceği mi: geri bugünden önceye, ileri son günden sonraya gitmez. */
export function stripBounds(from: string, today: string, lastDay: string): { prev: boolean; next: boolean } {
  return { prev: from > today, next: addDays(from, 7) <= lastDay };
}

const shortDay = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", weekday: "short" });

/** "2026-10-14" → { weekday: "Çar", day: "14" }. */
export function stripLabel(date: string): { weekday: string; day: string } {
  return { weekday: shortDay.format(new Date(`${date}T00:00:00Z`)), day: String(Number(date.slice(8, 10))) };
}

/** Sadece rakamlar; "0532 123 45 67" → "05321234567". */
export function digits(value: string): string {
  return value.replace(/\D/g, "");
}

/** TR cep telefonu mu (05xx… / 5xx… / +905…). Sunucu da doğrular; bu sadece erken uyarı. */
export function isMobile(value: string): boolean {
  const d = digits(value);
  const national = d.startsWith("90") && d.length === 12 ? d.slice(2) : d.startsWith("0") && d.length === 11 ? d.slice(1) : d;
  return national.length === 10 && national.startsWith("5");
}

const icsEscape = (s: string) => s.replace(/\\/g, "\\\\").replace(/;/g, "\\;").replace(/,/g, "\\,").replace(/\r?\n/g, "\\n");
const icsTime = (iso: string) => iso.replace(/[-:]/g, "").replace(/\.\d{3}/, "");

/** "Takvime ekle": tek etkinlikli .ics (RFC 5545), istemcide üretilir. Saatler UTC. */
export function buildIcs(e: { uid: string; title: string; startAt: string; endAt: string; location?: string | null; description?: string | null }): string {
  const lines = [
    "BEGIN:VCALENDAR",
    "VERSION:2.0",
    "PRODID:-//KobiFabrika//Online Randevu//TR",
    "CALSCALE:GREGORIAN",
    "METHOD:PUBLISH",
    "BEGIN:VEVENT",
    `UID:${e.uid}@kobifabrika.com`,
    `DTSTAMP:${icsTime(new Date(e.startAt).toISOString())}`,
    `DTSTART:${icsTime(new Date(e.startAt).toISOString())}`,
    `DTEND:${icsTime(new Date(e.endAt).toISOString())}`,
    `SUMMARY:${icsEscape(e.title)}`,
    ...(e.location ? [`LOCATION:${icsEscape(e.location)}`] : []),
    ...(e.description ? [`DESCRIPTION:${icsEscape(e.description)}`] : []),
    "END:VEVENT",
    "END:VCALENDAR",
  ];
  return lines.join("\r\n") + "\r\n";
}

/** Seçilen gün + "HH:mm" → UTC ISO (İstanbul saati). */
export function slotToUtc(date: string, time: string): string {
  return istanbulToUtcIso(date, time);
}
