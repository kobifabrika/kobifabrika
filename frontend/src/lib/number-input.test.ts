import { describe, expect, it } from "vitest";

import { formatNumberText, formatNumberValue, parseNumberText } from "./number-input";
import { parseQuantity } from "./sales";
import { parseMoneyInput } from "./validation/rules";

const value = (t: string) => parseNumberText(t)?.value;

describe("parseNumberText (virgül ondalık, nokta binlik)", () => {
  it("istenen örnekler", () => {
    expect(value("32.500")).toBe(32500);
    expect(value("32.500,75")).toBe(32500.75);
    expect(value("12.5")).toBe(12.5);
    expect(value("1.250.000")).toBe(1250000);
    expect(value("1500")).toBe(1500);
    expect(value("1.500,00")).toBe(1500);
  });

  it("virgülsüz noktadan sonra 1-2 rakam ondalık, 3 rakam binlik", () => {
    expect(value("12.50")).toBe(12.5);
    expect(value("0.5")).toBe(0.5);
    expect(value("0.500")).toBe(500);
    expect(value("1.250.50")).toBe(1250.5);
    expect(value("12.")).toBe(12);
  });

  it("ondalık hane sayısı korunur, okunamayan null", () => {
    expect(parseNumberText("10,001")).toEqual({ value: 10.001, decimals: 3 });
    expect(parseNumberText("abc")).toBeNull();
    expect(parseNumberText("")).toBeNull();
    expect(value("-300")).toBe(-300);
    expect(value("1 250 TL")).toBe(1250);
  });

  it("para, miktar ve gün alanları aynı kuralla okur", () => {
    for (const t of ["32.500", "32.500,75", "12.5", "1.250.000", "1500", "1.500,00"]) {
      expect(parseMoneyInput(t)?.value).toBe(value(t));
      expect(parseQuantity(t)).toBe(value(t));
    }
    expect(parseQuantity("-2")).toBeNull();
  });
});

describe("formatNumberText (yazarken)", () => {
  it("harf, boşluk, TL girilemez", () => {
    expect(formatNumberText("Ikln")).toBe("");
    expect(formatNumberText("12a5")).toBe("125");
    expect(formatNumberText("1 250 TL")).toBe("1.250");
  });

  it("binlikler noktayla gruplanır", () => {
    expect(formatNumberText("32500")).toBe("32.500");
    expect(formatNumberText("3.2500")).toBe("32.500");
    expect(formatNumberText("1500,5")).toBe("1.500,5");
    expect(formatNumberText("1.500,00")).toBe("1.500,00");
    expect(formatNumberText("1250000")).toBe("1.250.000");
  });

  it("virgülden sonra ikinci ayırıcı girilemez", () => {
    expect(formatNumberText("12,5.")).toBe("12,5");
    expect(formatNumberText("12,5,")).toBe("12,5");
    expect(formatNumberText(",5")).toBe("0,5");
  });

  it("bekleyen ondalık nokta olduğu gibi kalır (32.5 → 32.500 yazılıyor olabilir)", () => {
    expect(formatNumberText("32.")).toBe("32.");
    expect(formatNumberText("32.5")).toBe("32.5");
    expect(formatNumberText("32.50")).toBe("32.50");
    expect(formatNumberText("32.500")).toBe("32.500");
  });

  it("tam sayı alanında sadece rakam, eksi sadece izinli alanda", () => {
    expect(formatNumberText("14,5", { kind: "integer" })).toBe("145");
    expect(formatNumberText("1.000", { kind: "integer" })).toBe("1000");
    expect(formatNumberText("-2,5")).toBe("2,5");
    expect(formatNumberText("-2,5", { allowNegative: true })).toBe("-2,5");
  });
});

describe("formatNumberValue (odaktan çıkınca)", () => {
  it("para alanında iki ondalık ve binlik", () => {
    expect(formatNumberValue("32.500", { fixed: 2 })).toBe("32.500,00");
    expect(formatNumberValue("32.500,75", { fixed: 2 })).toBe("32.500,75");
    expect(formatNumberValue("12.5", { fixed: 2 })).toBe("12,50");
    expect(formatNumberValue("1500", { fixed: 2 })).toBe("1.500,00");
  });

  it("miktar alanında gereksiz ondalık eklenmez; fazla ondalıkta metin kalır", () => {
    expect(formatNumberValue("12.5")).toBe("12,5");
    expect(formatNumberValue("1250000")).toBe("1.250.000");
    expect(formatNumberValue("10,001", { fixed: 2 })).toBe("10,001");
    expect(formatNumberValue("abc")).toBe("abc");
  });
});
