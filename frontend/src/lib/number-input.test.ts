import { describe, expect, it } from "vitest";

import { normalizeNumberText } from "./number-input";
import { parseMoneyInput } from "./validation/rules";

describe("normalizeNumberText", () => {
  it("harf, boşluk ve TL girilemez", () => {
    expect(normalizeNumberText("Ikln")).toBe("");
    expect(normalizeNumberText("12a5")).toBe("125");
    expect(normalizeNumberText("1 250 TL")).toBe("1250");
    expect(normalizeNumberText("₺99")).toBe("99");
  });

  it("klavyeden yazılan nokta virgüle çevrilir, ikinci ayırıcı girilemez", () => {
    expect(normalizeNumberText("12.", { typed: true })).toBe("12,");
    expect(normalizeNumberText("12,5.", { typed: true })).toBe("12,5");
    expect(normalizeNumberText("12,5,", { typed: true })).toBe("12,5");
    expect(normalizeNumberText(",", { typed: true })).toBe("0,");
  });

  it("yapıştırmada nokta binlik ayırıcı sayılır (parseMoneyInput ile aynı)", () => {
    expect(normalizeNumberText("1.500,00")).toBe("1500,00");
    expect(normalizeNumberText("1.500.000")).toBe("1500000");
    expect(normalizeNumberText("32.500")).toBe("32500");
    expect(normalizeNumberText("12.5")).toBe("12,5");
    expect(normalizeNumberText("0.500")).toBe("0,500");
    for (const text of ["1.500,00", "1.500.000", "32.500", "12.5"])
      expect(parseMoneyInput(normalizeNumberText(text))?.value).toBe(parseMoneyInput(text)?.value);
  });

  it("tam sayı alanında (gün, adet, dakika) sadece rakam", () => {
    expect(normalizeNumberText("14,5", { kind: "integer" })).toBe("145");
    expect(normalizeNumberText("3 gün", { kind: "integer" })).toBe("3");
    expect(normalizeNumberText("-3", { kind: "integer" })).toBe("3");
  });

  it("eksi sadece izin verilen alanda ve sadece başta", () => {
    expect(normalizeNumberText("-2,5")).toBe("2,5");
    expect(normalizeNumberText("-2,5", { allowNegative: true })).toBe("-2,5");
    expect(normalizeNumberText("2-5", { allowNegative: true })).toBe("25");
  });
});
