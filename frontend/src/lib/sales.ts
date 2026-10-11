/**
 * Satış ve teklif tutarları (backend Domain/Sales/SaleCalculator.cs ile aynı algoritma ve test vektörleri).
 * Fiyatlar KDV dahildir; KDV satır tutarının içinden ayrılır: KDV = tutar × oran / (100 + oran).
 * Toplam indirim satırlara orantılı dağıtılır; yuvarlama farkı en büyük satıra yazılır. Yuvarlama 2 ondalık, yarım yukarı.
 * Sunucu satışı kaydederken aynı hesabı yeniden yapar; buradaki sonuç sadece önizlemedir.
 */

export type DiscountType = "Amount" | "Percent";
export type Discount = { type: DiscountType; value: number };

export type CalcLineInput = { quantity: number; unitPrice: number; vatRate: number; discount?: Discount | null };

export type CalcLine = {
  gross: number;
  lineDiscount: number;
  allocatedDiscount: number;
  lineTotal: number;
  vatAmount: number;
};

export type VatRow = { rate: number; base: number; vat: number };

export type CalcResult = {
  lines: CalcLine[];
  subTotal: number;
  lineDiscountTotal: number;
  discountTotal: number;
  vatTotal: number;
  total: number;
  vat: VatRow[];
};

/** 2 ondalığa yarım yukarı yuvarlama; kayan nokta hatasına karşı (1.005 → 1.01). */
export function round2(value: number): number {
  const sign = value < 0 ? -1 : 1;
  return (sign * Math.round(Number((Math.abs(value) * 100).toPrecision(15)))) / 100;
}

const sum = (values: number[]) => round2(values.reduce((a, b) => a + b, 0));

/** KDV hariç fiyatı KDV dahile çevirir. */
export function addVat(net: number, vatRate: number): number {
  return round2((net * (100 + vatRate)) / 100);
}

/** KDV dahil tutarın içindeki KDV. */
export function vatOf(gross: number, vatRate: number): number {
  return vatRate === 0 ? 0 : round2((gross * vatRate) / (100 + vatRate));
}

/** İndirimin TL karşılığı; tabanı aşamaz. */
export function discountAmount(discount: Discount | null | undefined, base: number): number {
  if (!discount || base <= 0) return 0;
  const amount = discount.type === "Percent" ? round2((base * discount.value) / 100) : discount.value;
  return Math.min(Math.max(amount, 0), base);
}

export function calculate(lines: CalcLineInput[], totalDiscount?: Discount | null): CalcResult {
  const gross = lines.map((l) => round2(l.quantity * l.unitPrice));
  const lineDiscounts = lines.map((l, i) => discountAmount(l.discount, gross[i]));
  const afterLine = gross.map((g, i) => round2(g - lineDiscounts[i]));
  const base = sum(afterLine);

  const discountTotal = discountAmount(totalDiscount, base);
  const allocated = lines.map(() => 0);
  if (discountTotal > 0) {
    for (let i = 0; i < lines.length; i++) allocated[i] = round2((discountTotal * afterLine[i]) / base);
    const largest = afterLine.indexOf(Math.max(...afterLine));
    allocated[largest] = round2(allocated[largest] + discountTotal - sum(allocated));
  }

  const result = lines.map((l, i) => {
    const lineTotal = round2(afterLine[i] - allocated[i]);
    return {
      gross: gross[i],
      lineDiscount: lineDiscounts[i],
      allocatedDiscount: allocated[i],
      lineTotal,
      vatAmount: vatOf(lineTotal, l.vatRate),
    };
  });

  const byRate = new Map<number, VatRow>();
  result.forEach((r, i) => {
    const rate = lines[i].vatRate;
    const row = byRate.get(rate) ?? { rate, base: 0, vat: 0 };
    row.base = round2(row.base + r.lineTotal - r.vatAmount);
    row.vat = round2(row.vat + r.vatAmount);
    byRate.set(rate, row);
  });

  return {
    lines: result,
    subTotal: sum(gross),
    lineDiscountTotal: sum(lineDiscounts),
    discountTotal,
    vatTotal: sum(result.map((r) => r.vatAmount)),
    total: sum(result.map((r) => r.lineTotal)),
    vat: [...byRate.values()].sort((a, b) => a.rate - b.rate),
  };
}

// Miktar (backend Features/Catalog/CatalogModels.cs → Quantities ile aynı kurallar ve mesajlar).

export type Unit = "Piece" | "Kg" | "Liter" | "Meter" | "Package" | "Hour" | "Session";

export const unitNames: Record<Unit, string> = {
  Piece: "Adet",
  Kg: "Kg",
  Liter: "Lt",
  Meter: "M",
  Package: "Paket",
  Hour: "Saat",
  Session: "Seans",
};

export const QUANTITY_MAX = 9_999_999;

/** Kg, lt, m ondalık miktar alır; diğerleri tam sayı. */
export function allowsFraction(unit: Unit): boolean {
  return unit === "Kg" || unit === "Liter" || unit === "Meter";
}

/** Kullanıcının yazdığı miktarı çözer: "1,5", "2", "0.75". */
export function parseQuantity(text: string | null | undefined): number | null {
  const t = (text ?? "").replace(/\s/g, "").replace(",", ".");
  if (!/^\d+(\.\d+)?$/.test(t)) return null;
  return Number(t);
}

export function validateQuantity(value: number | null, unit: Unit, allowZero = false): string | null {
  if (value === null || Number.isNaN(value)) return "Miktar girin.";
  if (value < 0) return "Miktar eksi olamaz.";
  if (value === 0 && !allowZero) return "Miktar 0'dan büyük olmalı.";
  if (value > QUANTITY_MAX) return "Miktar çok büyük.";
  if (Math.round(value * 1000) / 1000 !== value) return "Miktar en fazla 3 ondalık basamak içerebilir.";
  if (!allowsFraction(unit) && !Number.isInteger(value)) return `${unitNames[unit]} için tam sayı girin.`;
  return null;
}

const quantityFormat = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 3 });

/** 1.5, "Kg" → "1,5 kg"; 3, "Piece" → "3 adet". */
export function formatQuantity(value: number, unit: Unit): string {
  return `${quantityFormat.format(value)} ${unitNames[unit].toLocaleLowerCase("tr-TR")}`;
}
