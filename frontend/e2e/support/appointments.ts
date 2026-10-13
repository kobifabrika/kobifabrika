import type { ApiUser } from "./api";

/** Sprint 5 personel ve randevu verisini API ile hazırlama yardımcıları (normal uçlar; arka kapı yok). */

export type Staff = { id: string; fullName: string };
export type Appointment = { id: string; startAt: string; endAt: string; status: string; staffMemberId: string };

export function createService(user: ApiUser, name = "Saç kesimi", durationMinutes = 45, salePrice = 300) {
  return user.post<{ id: string; name: string }>("/api/products", {
    type: "Service",
    name,
    categoryId: null,
    unit: "Piece",
    vatRate: 20,
    pricesIncludeVat: true,
    salePrice,
    purchasePrice: null,
    barcode: null,
    durationMinutes,
    trackStock: false,
    criticalStockLevel: null,
    initialStock: null,
    notes: null,
  });
}

export function createStaff(user: ApiUser, fullName: string, userId: string | null = null) {
  return user.post<Staff>("/api/staff", { fullName, title: "Kuaför", phone: null, color: "Black", showInBooking: true, userId, isActive: true });
}

/** Personelin haftanın her günü (Pazar dahil) 00:00-23:59 çalışması: testler saatten bağımsız olsun. */
export function workAllDay(user: ApiUser, staffId: string) {
  const days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"].map((day) => ({
    day,
    ranges: [{ start: "00:00", end: "23:59" }],
  }));
  return user.put(`/api/staff/${staffId}/working-hours`, { days });
}

/** İşletmeyi her gün 00:00-23:59 açık yapar (takvim ızgarası ve çalışma saati uyarısı saatten bağımsız olsun). */
export function openAllDay(user: ApiUser) {
  const days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"].map((day) => ({
    day,
    isClosed: false,
    opensAt: "00:00",
    closesAt: "23:59",
  }));
  return user.put("/api/business/working-hours", { days });
}

/** İstanbul günü + "HH:mm" → UTC ISO. */
export function at(day: string, time: string): string {
  return new Date(`${day}T${time}:00+03:00`).toISOString().replace(".000Z", "Z");
}

export async function book(
  user: ApiUser,
  data: { contactId: string; staffId: string; serviceId: string; startAt: string; endAt?: string | null; note?: string | null },
) {
  const result = await user.post<{ appointment: Appointment }>("/api/appointments", {
    contactId: data.contactId,
    staffMemberId: data.staffId,
    productId: data.serviceId,
    startAt: data.startAt,
    endAt: data.endAt ?? null,
    note: data.note ?? null,
  });
  return result.appointment;
}

/**
 * Şimdiden en az minutesAgo önceki, 5 dakikaya yuvarlanmış bir an (İstanbul günü ve saati) — "Tamamlandı" işaretlenebilsin.
 * Randevu gece yarısını aşmasın diye saat 23:00'ten önceye çekilir.
 */
export function pastSlot(minutesAgo: number): { day: string; time: string; iso: string } {
  let d = new Date(Date.now() - minutesAgo * 60_000);
  while (new Intl.DateTimeFormat("en-GB", { timeZone: "Europe/Istanbul", hour: "2-digit", hourCycle: "h23" }).format(d) >= "23") {
    d = new Date(d.getTime() - 60 * 60_000);
  }
  d.setUTCMinutes(Math.floor(d.getUTCMinutes() / 5) * 5, 0, 0);
  const day = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(d);
  const time = new Intl.DateTimeFormat("en-GB", { timeZone: "Europe/Istanbul", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(d);
  return { day, time, iso: d.toISOString() };
}
