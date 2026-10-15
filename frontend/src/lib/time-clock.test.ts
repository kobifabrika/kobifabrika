import { describe, expect, it } from "vitest";

import {
  addMonths,
  dateRangeText,
  daysText,
  elapsedMinutes,
  formatDuration,
  hoursText,
  inRange,
  monthDays,
  monthLabel,
  overtimeMinutes,
} from "./time-clock";

describe("mesai süreleri", () => {
  it("süreyi saat ve dakika olarak yazar", () => {
    expect(formatDuration(192)).toBe("3 sa 12 dk");
    expect(formatDuration(45)).toBe("45 dk");
    expect(formatDuration(120)).toBe("2 sa");
    expect(formatDuration(0)).toBe("0 dk");
    expect(formatDuration(-5)).toBe("0 dk");
  });

  it("rapor saatleri Türkçe ondalıkla", () => {
    expect(hoursText(2730)).toBe("45,5");
    expect(hoursText(2700)).toBe("45");
    expect(hoursText(20)).toBe("0,33");
  });

  it("açık mesainin geçen süresi", () => {
    const start = "2026-10-15T06:00:00Z";
    expect(elapsedMinutes(start, Date.parse("2026-10-15T09:12:30Z"))).toBe(192);
    expect(elapsedMinutes(start, Date.parse("2026-10-15T05:00:00Z"))).toBe(0);
  });

  it("fazla mesai 45 saat sınırı (backend testleriyle aynı)", () => {
    expect(overtimeMinutes(2699, 45)).toBe(0);
    expect(overtimeMinutes(2700, 45)).toBe(0);
    expect(overtimeMinutes(2730, 45)).toBe(30);
    expect(overtimeMinutes(2700, 37.5)).toBe(450);
  });
});

describe("izin metinleri ve ay görünümü", () => {
  it("gün sayısı", () => {
    expect(daysText(1)).toBe("1 gün");
    expect(daysText(0.5)).toBe("Yarım gün");
    expect(daysText(3.5)).toBe("3,5 gün");
    expect(daysText(-2)).toBe("-2 gün");
  });

  it("tarih aralığı", () => {
    expect(dateRangeText("2026-11-03", "2026-11-03")).toBe("3 Kasım 2026");
    expect(dateRangeText("2026-11-03", "2026-11-05")).toBe("3 – 5 Kasım 2026");
    expect(dateRangeText("2026-10-30", "2026-11-02")).toBe("30 Ekim – 2 Kasım 2026");
    expect(dateRangeText("2026-12-30", "2027-01-02")).toBe("30 Aralık 2026 – 2 Ocak 2027");
  });

  it("ayın günleri, ay geçişi ve etiket", () => {
    expect(monthDays("2026-11")).toHaveLength(30);
    expect(monthDays("2028-02").at(-1)).toBe("2028-02-29");
    expect(addMonths("2026-12", 1)).toBe("2027-01");
    expect(addMonths("2026-01", -1)).toBe("2025-12");
    expect(monthLabel("2026-11")).toBe("Kasım 2026");
    expect(inRange("2026-11-03", "2026-11-03", "2026-11-05")).toBe(true);
    expect(inRange("2026-11-06", "2026-11-03", "2026-11-05")).toBe(false);
  });
});
