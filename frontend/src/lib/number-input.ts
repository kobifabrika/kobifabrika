/**
 * Para, adet, gün ve yüzde alanlarına yazılan metni süzer (2026-10-21): harf, boşluk ve "TL" girilemez; sadece rakam ve tek ondalık
 * ayırıcı (virgül). Klavyeden nokta yazılırsa virgüle çevrilir. Yapıştırılan "1.500,00" / "1.500.000" gibi metinlerde nokta binlik
 * ayırıcı sayılır (`parseMoneyInput` ile aynı kural). Sonuç her zaman `parseMoneyInput` / `parseQuantity` ile okunabilir.
 */
export type NumberKind = "decimal" | "integer";

export type NumberTextOptions = {
  kind?: NumberKind;
  /** Eksi değer girilebilir (ör. izin bakiyesi düzeltmesi, kasa açılış bakiyesi). */
  allowNegative?: boolean;
  /** Tek karakter yazıldı (klavye). false: yapıştırma, otomatik doldurma ya da toplu değişiklik. */
  typed?: boolean;
};

export function normalizeNumberText(raw: string, { kind = "decimal", allowNegative = false, typed = false }: NumberTextOptions = {}): string {
  let t = raw.replace(/[^\d.,-]/g, "");
  const negative = allowNegative && t.startsWith("-");
  t = t.replace(/-/g, "");

  if (kind === "integer") {
    t = t.replace(/[.,]/g, "");
  } else if (typed) {
    t = t.replace(/\./g, ",");
  } else if (t.includes(",")) {
    t = t.replace(/\./g, "");
  } else if (t.includes(".")) {
    const parts = t.split(".");
    const thousands = parts.length > 2 || (parts[1].length === 3 && parts[0].length > 0 && parts[0] !== "0");
    t = thousands ? t.replace(/\./g, "") : t.replace(".", ",");
  }

  const comma = t.indexOf(",");
  if (comma >= 0) t = t.slice(0, comma + 1) + t.slice(comma + 1).replace(/,/g, "");
  if (t.startsWith(",")) t = `0${t}`;
  return (negative ? "-" : "") + t;
}
