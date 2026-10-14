import { describe, expect, it } from "vitest";

import { buildIcs, groupSlots, isBookingStep, isMobile, slotToUtc, stripBounds, stripLabel, weekFrom } from "./booking";

describe("online randevu yardımcıları", () => {
  it("boş saatleri sabah, öğle ve akşam gruplarına ayırır", () => {
    expect(groupSlots(["09:00", "11:45", "12:00", "16:59", "17:00", "20:30"])).toEqual([
      { label: "Sabah", times: ["09:00", "11:45"] },
      { label: "Öğle", times: ["12:00", "16:59"] },
      { label: "Akşam", times: ["17:00", "20:30"] },
    ]);
    expect(groupSlots(["13:00"])).toEqual([{ label: "Öğle", times: ["13:00"] }]);
    expect(groupSlots([])).toEqual([]);
  });

  it("gün şeridi 7 gün, ileri/geri sınırları bugün ve son gün", () => {
    expect(weekFrom("2026-10-28")).toEqual([
      "2026-10-28",
      "2026-10-29",
      "2026-10-30",
      "2026-10-31",
      "2026-11-01",
      "2026-11-02",
      "2026-11-03",
    ]);
    expect(stripBounds("2026-10-14", "2026-10-14", "2026-11-13")).toEqual({ prev: false, next: true });
    expect(stripBounds("2026-10-21", "2026-10-14", "2026-10-27")).toEqual({ prev: true, next: false });
    expect(stripLabel("2026-10-14")).toEqual({ weekday: "Çar", day: "14" });
  });

  it("adım adı doğrulanır", () => {
    expect(isBookingStep("saat")).toBe(true);
    expect(isBookingStep("odeme")).toBe(false);
    expect(isBookingStep(null)).toBe(false);
  });

  it("TR cep telefonu", () => {
    expect(isMobile("0532 123 45 67")).toBe(true);
    expect(isMobile("532 123 45 67")).toBe(true);
    expect(isMobile("+90 532 123 45 67")).toBe(true);
    expect(isMobile("0212 555 44 33")).toBe(false);
    expect(isMobile("0532 123")).toBe(false);
  });

  it("İstanbul saati UTC'ye çevrilir ve .ics etkinliği üretilir", () => {
    const start = slotToUtc("2026-10-14", "14:30");
    expect(start).toBe("2026-10-14T11:30:00.000Z");
    const ics = buildIcs({
      uid: "abc",
      title: "Saç kesimi, Şık Kuaför",
      startAt: start,
      endAt: "2026-10-14T12:15:00.000Z",
      location: "Moda Cd. 1; Kadıköy",
    });
    expect(ics).toContain("DTSTART:20261014T113000Z\r\n");
    expect(ics).toContain("DTEND:20261014T121500Z\r\n");
    expect(ics).toContain("SUMMARY:Saç kesimi\\, Şık Kuaför\r\n");
    expect(ics).toContain("LOCATION:Moda Cd. 1\\; Kadıköy\r\n");
    expect(ics.startsWith("BEGIN:VCALENDAR\r\n")).toBe(true);
    expect(ics.endsWith("END:VCALENDAR\r\n")).toBe(true);
  });
});
