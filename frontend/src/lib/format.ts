const TIME_ZONE = "Europe/Istanbul";

const dateTime = new Intl.DateTimeFormat("tr-TR", {
  timeZone: TIME_ZONE,
  day: "numeric",
  month: "long",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

const date = new Intl.DateTimeFormat("tr-TR", { timeZone: TIME_ZONE, day: "numeric", month: "long", year: "numeric" });

/** UTC ISO tarihini İstanbul saatiyle gösterir: "10 Ekim 2026 16:45". */
export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso));
}

export function formatDate(iso: string): string {
  return date.format(new Date(iso));
}

/** "az önce", "5 dk önce", "3 sa önce", aksi halde tarih. */
export function formatRelative(iso: string, now = new Date()): string {
  const minutes = Math.floor((now.getTime() - new Date(iso).getTime()) / 60_000);
  if (minutes < 1) return "az önce";
  if (minutes < 60) return `${minutes} dk önce`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} sa önce`;
  return formatDateTime(iso);
}

const hourFormat = new Intl.DateTimeFormat("en-GB", { timeZone: TIME_ZONE, hour: "numeric", hourCycle: "h23" });

/** İstanbul saatine göre selamlama: "Günaydın", "İyi günler", "İyi akşamlar", "İyi geceler". */
export function greeting(now = new Date()): string {
  const hour = Number(hourFormat.format(now));
  if (hour >= 5 && hour < 12) return "Günaydın";
  if (hour >= 12 && hour < 18) return "İyi günler";
  if (hour >= 18 && hour < 23) return "İyi akşamlar";
  return "İyi geceler";
}

const money = new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** 1250.5 → "1.250,50 TL". */
export function formatMoney(value: number): string {
  return `${money.format(value)} TL`;
}

/**
 * Cari bakiye dili (backend ile aynı): pozitif = müşteri bize borçlu → "Alacak", negatif = biz borçluyuz → "Borç".
 * Renk kullanılmaz; ayrım kelimeyle yapılır (docs/04-tasarim.md).
 */
export function balanceLabel(balance: number): string {
  if (balance > 0) return `Alacak ${formatMoney(balance)}`;
  if (balance < 0) return `Borç ${formatMoney(-balance)}`;
  return "Bakiye yok";
}

const day = new Intl.DateTimeFormat("tr-TR", { timeZone: "UTC", day: "numeric", month: "long", year: "numeric" });
const dayShort = new Intl.DateTimeFormat("tr-TR", {
  timeZone: "UTC",
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
});

/** "2026-05-14" (takvim günü, saat dilimsiz) → "14 Mayıs 2026". */
export function formatDay(isoDate: string): string {
  return day.format(new Date(`${isoDate}T00:00:00Z`));
}

/** "2026-05-14" → "14.05.2026". */
export function formatDayShort(isoDate: string): string {
  return dayShort.format(new Date(`${isoDate}T00:00:00Z`));
}

const isoDay = new Intl.DateTimeFormat("en-CA", {
  timeZone: TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});
const isoTime = new Intl.DateTimeFormat("en-GB", {
  timeZone: TIME_ZONE,
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

/** İstanbul'a göre bugünün tarihi: "2026-10-11". */
export function todayIstanbul(now = new Date()): string {
  return isoDay.format(now);
}

/** UTC zamanın İstanbul'daki gün ve saati: { date: "2026-10-11", time: "14:30" }. */
export function istanbulParts(iso: string): { date: string; time: string } {
  const d = new Date(iso);
  return { date: isoDay.format(d), time: isoTime.format(d) };
}

/** İstanbul gün + saatini UTC ISO'ya çevirir. Türkiye 2016'dan beri yıl boyu UTC+3. */
export function istanbulToUtcIso(date: string, time: string): string {
  return new Date(`${date}T${time}:00+03:00`).toISOString();
}
