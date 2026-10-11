import { describe, expect, it } from "vitest";

import { addVat, calculate, formatQuantity, parseQuantity, round2, validateQuantity } from "./sales";

// Backend tests/Domain/SaleCalculatorTests.cs ile aynı vektörler.
describe("calculate", () => {
  it.each([
    [100, 20, 120],
    [99.99, 20, 119.99],
    [50, 10, 55],
    [80, 0, 80],
    [33.33, 1, 33.66],
  ])("addVat(%d, %d) = %d", (net, rate, expected) => {
    expect(addVat(net, rate)).toBe(expected);
  });

  it("extracts VAT from vat-inclusive line", () => {
    const r = calculate([{ quantity: 2, unitPrice: 60, vatRate: 20 }]);
    expect(r).toMatchObject({ subTotal: 120, total: 120, vatTotal: 20 });
    expect(r.vat).toEqual([{ rate: 20, base: 100, vat: 20 }]);
  });

  it("applies line and total discounts and allocates", () => {
    const r = calculate(
      [
        { quantity: 1, unitPrice: 100, vatRate: 20 },
        { quantity: 3, unitPrice: 10, vatRate: 10, discount: { type: "Percent", value: 10 } },
      ],
      { type: "Amount", value: 12.7 },
    );
    expect(r).toMatchObject({ subTotal: 130, lineDiscountTotal: 3, discountTotal: 12.7, total: 114.3, vatTotal: 17.21 });
    expect(r.lines[0]).toMatchObject({ allocatedDiscount: 10, lineTotal: 90, vatAmount: 15 });
    expect(r.lines[1]).toMatchObject({ allocatedDiscount: 2.7, lineTotal: 24.3, vatAmount: 2.21 });
  });

  it("gives rounding remainder to the largest line", () => {
    const line = { quantity: 1, unitPrice: 10, vatRate: 20 };
    const r = calculate([line, line, line], { type: "Amount", value: 10 });
    expect(r.lines.map((l) => l.allocatedDiscount)).toEqual([3.34, 3.33, 3.33]);
    expect(r.lines.map((l) => l.lineTotal)).toEqual([6.66, 6.67, 6.67]);
    expect(r.total).toBe(20);
    expect(r.vatTotal).toBe(3.33);
  });

  it("never discounts more than the base", () => {
    const r = calculate(
      [
        { quantity: 1, unitPrice: 50, vatRate: 20, discount: { type: "Amount", value: 80 } },
        { quantity: 1, unitPrice: 40, vatRate: 20 },
      ],
      { type: "Percent", value: 150 },
    );
    expect(r).toMatchObject({ lineDiscountTotal: 50, discountTotal: 40, total: 0, vatTotal: 0 });
  });

  it("rounds fractional quantities to kuruş", () => {
    const r = calculate([{ quantity: 1.5, unitPrice: 89.9, vatRate: 1 }]);
    expect(r.total).toBe(134.85);
    expect(r.vatTotal).toBe(1.34);
  });

  it("rounds half up despite floating point", () => {
    expect(round2(1.005)).toBe(1.01);
    expect(round2(0.1 + 0.2)).toBe(0.3);
  });
});

describe("quantity", () => {
  it("parses Turkish decimals", () => {
    expect(parseQuantity("1,5")).toBe(1.5);
    expect(parseQuantity(" 2 ")).toBe(2);
    expect(parseQuantity("abc")).toBeNull();
  });

  it("validates per unit like the backend", () => {
    expect(validateQuantity(1.5, "Piece")).toBe("Adet için tam sayı girin.");
    expect(validateQuantity(1.5, "Kg")).toBeNull();
    expect(validateQuantity(0, "Kg")).toBe("Miktar 0'dan büyük olmalı.");
    expect(validateQuantity(0, "Kg", true)).toBeNull();
    expect(validateQuantity(1.2345, "Kg")).toBe("Miktar en fazla 3 ondalık basamak içerebilir.");
  });

  it("formats with unit", () => {
    expect(formatQuantity(1.5, "Kg")).toBe("1,5 kg");
    expect(formatQuantity(3, "Piece")).toBe("3 adet");
  });
});
