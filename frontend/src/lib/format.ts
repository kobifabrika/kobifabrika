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
