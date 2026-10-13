import { describe, expect, it } from "vitest";

import { noShowText } from "@/components/appointments/appointment-form";

import { dayError } from "./staff-hours";

describe("personel çalışma saatleri", () => {
  it("öğle arası iki aralık geçerli; çakışan ve ters aralık hatalı", () => {
    const day = (ranges: { start: string; end: string }[]) => ({ day: "Monday" as const, ranges });
    expect(dayError(day([{ start: "09:00", end: "12:00" }, { start: "13:00", end: "18:00" }]))).toBeNull();
    expect(dayError(day([]))).toBeNull();
    expect(dayError(day([{ start: "09:00", end: "12:00" }, { start: "11:30", end: "18:00" }]))).toBe("Aralıklar çakışıyor.");
    expect(dayError(day([{ start: "12:00", end: "09:00" }]))).toBe("Bitiş saati başlangıçtan sonra olmalı.");
    expect(dayError(day([{ start: "", end: "09:00" }]))).toBe("Saatleri girin.");
  });
});

describe("gelmeyen müşteri uyarısı", () => {
  it("son randevulardan kaçına gelmediğini yazar", () => {
    expect(noShowText("Ayşe Yılmaz", { noShows: 2, of: 5 })).toBe("Ayşe Yılmaz son 5 randevusundan 2'sine gelmedi.");
    expect(noShowText("Can", { noShows: 3, of: 4 })).toBe("Can son 4 randevusundan 3'üne gelmedi.");
    expect(noShowText("Can", { noShows: 5, of: 5 })).toBe("Can son 5 randevusundan 5'ine gelmedi.");
  });
});
