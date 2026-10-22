import { describe, expect, it } from "vitest";

import {
  balanceLabel,
  formatDay,
  formatMoney,
  greeting,
  istanbulParts,
  istanbulToUtcIso,
  todayIstanbul,
} from "./format";

describe("greeting", () => {
  // İstanbul UTC+3: 05:00 UTC = 08:00, 01:00 UTC = 04:00, 02:00 UTC = 05:00 İstanbul.
  it.each([
    ["2026-10-10T05:00:00Z", "Günaydın"],
    ["2026-10-10T10:00:00Z", "İyi günler"],
    ["2026-10-10T16:30:00Z", "İyi akşamlar"],
    ["2026-10-10T21:00:00Z", "İyi geceler"],
    ["2026-10-10T01:00:00Z", "İyi geceler"],
    ["2026-10-10T02:00:00Z", "Günaydın"],
  ])("%s → %s (İstanbul saatine göre)", (iso, expected) => {
    expect(greeting(new Date(iso))).toBe(expected);
  });
});

describe("para ve cari bakiye dili", () => {
  // Tutar biçimi (karar 2026-10-22); aynı örnekler backend Domain/IbanAndMoneyTests.cs'te. Boşluk bölünmeyen boşluk (U+00A0).
  it.each([
    [1299, "1.299\u00a0TL"],
    [1250.5, "1.250,50\u00a0TL"],
    [699, "699\u00a0TL"],
    [0, "0\u00a0TL"],
    [0.5, "0,50\u00a0TL"],
    [12990, "12.990\u00a0TL"],
    [1234567.89, "1.234.567,89\u00a0TL"],
    [-300.5, "-300,50\u00a0TL"],
    [100.004, "100\u00a0TL"],
    [0.005, "0,01\u00a0TL"],
    [-0.001, "0\u00a0TL"],
  ])("formatMoney(%s) → %s", (value, expected) => {
    expect(formatMoney(value)).toBe(expected);
  });

  it("pozitif bakiye Alacak, negatif Borç (işletmenin bakışı)", () => {
    expect(balanceLabel(1250)).toBe("Alacak 1.250\u00a0TL");
    expect(balanceLabel(-300.5)).toBe("Borç 300,50\u00a0TL");
    expect(balanceLabel(0)).toBe("Bakiye yok");
  });
});

describe("İstanbul tarih/saat", () => {
  it("takvim günü saat diliminden etkilenmez", () => {
    expect(formatDay("2026-05-14")).toBe("14 Mayıs 2026");
  });

  it("gece yarısından sonra UTC'de dün olsa da İstanbul'da bugün", () => {
    expect(todayIstanbul(new Date("2026-10-10T22:30:00Z"))).toBe("2026-10-11");
  });

  it("İstanbul gün+saat ↔ UTC", () => {
    expect(istanbulToUtcIso("2026-10-11", "10:00")).toBe("2026-10-11T07:00:00.000Z");
    expect(istanbulParts("2026-10-11T07:00:00.000Z")).toEqual({ date: "2026-10-11", time: "10:00" });
  });
});
