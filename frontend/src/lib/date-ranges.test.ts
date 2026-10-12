import { describe, expect, it } from "vitest";

import { smsLink } from "./contacts";
import { addDays, presetRange, startOfWeek } from "./date-ranges";

describe("tarih aralığı ön ayarları (İstanbul günü)", () => {
  const today = "2026-10-14"; // Çarşamba

  it("bugün, bu hafta (Pazartesi), bu ay, bu yıl", () => {
    expect(presetRange("bugun", today)).toEqual({ from: today, to: today });
    expect(presetRange("bu-hafta", today)).toEqual({ from: "2026-10-12", to: today });
    expect(presetRange("bu-ay", today)).toEqual({ from: "2026-10-01", to: today });
    expect(presetRange("bu-yil", today)).toEqual({ from: "2026-01-01", to: today });
  });

  it("geçen ay ay ve yıl sınırında doğru", () => {
    expect(presetRange("gecen-ay", today)).toEqual({ from: "2026-09-01", to: "2026-09-30" });
    expect(presetRange("gecen-ay", "2026-01-05")).toEqual({ from: "2025-12-01", to: "2025-12-31" });
    expect(presetRange("gecen-ay", "2028-03-01")).toEqual({ from: "2028-02-01", to: "2028-02-29" });
  });

  it("özel aralık ters girilirse düzeltilir, eksikse bu aya döner", () => {
    expect(presetRange("ozel", today, { from: "2026-10-10", to: "2026-10-01" })).toEqual({ from: "2026-10-01", to: "2026-10-10" });
    expect(presetRange("ozel", today, {})).toEqual({ from: "2026-10-01", to: today });
  });

  it("hafta Pazartesi başlar (Pazar günü dahil)", () => {
    expect(startOfWeek("2026-10-18")).toBe("2026-10-12");
    expect(startOfWeek("2026-10-12")).toBe("2026-10-12");
    expect(startOfWeek("2027-01-01")).toBe("2026-12-28");
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
  });

  it("İstanbul gece yarısından sonra bugün ertesi gündür", async () => {
    const { todayIstanbul } = await import("./format");
    // 2026-10-12 21:30 UTC = İstanbul 13 Ekim 00:30
    expect(todayIstanbul(new Date("2026-10-12T21:30:00Z"))).toBe("2026-10-13");
    expect(todayIstanbul(new Date("2026-10-12T20:30:00Z"))).toBe("2026-10-12");
  });
});

describe("SMS linki", () => {
  it("telefonun SMS uygulamasını hazır metinle açar", () => {
    expect(smsLink("+905321234567", "Merhaba Ayşe & ekip")).toBe("sms:+905321234567?&body=Merhaba%20Ay%C5%9Fe%20%26%20ekip");
    expect(smsLink(null, "x")).toBe("sms:?&body=x");
  });
});
