/**
 * Sayı alanları için Türkçe okuma ve gösterim kuralı (2026-10-21): virgül her zaman ondalık ayırıcı, nokta binlik ayırıcı.
 * Tek istisna: virgül yoksa ve son noktadan sonra 1-2 rakam varsa (Android'de sadece nokta tuşu çıkan klavyeler) o nokta
 * ondalıktır: "12.5" → 12,5. Noktadan sonra 3 rakam varsa binliktir: "32.500" → 32500. Harf, boşluk ve "TL" girilemez.
 * Para, adet, gün ve yüzde alanları aynı kuralı kullanır (`parseMoneyInput`, `parseQuantity` buradan okur).
 */
export type NumberKind = "decimal" | "integer";

export type NumberTextOptions = {
  kind?: NumberKind;
  /** Eksi değer girilebilir (ör. izin bakiyesi düzeltmesi). */
  allowNegative?: boolean;
};

const group = (digits: string) => {
  const d = digits.replace(/^0+(?=\d)/, "");
  return d.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
};

/** Virgülsüz metinde son nokta ondalık mı: son grup 1-2 rakam ve önceki gruplar (ilki hariç) tam 3 rakam. */
function decimalDot(parts: string[]): boolean {
  const last = parts[parts.length - 1]!;
  return last.length >= 1 && last.length <= 2 && parts.slice(1, -1).every((p) => p.length === 3);
}

/** "32.500" → 32500, "32.500,75" → 32500.75, "12.5" → 12.5, "1.250.000" → 1250000; okunamazsa null. Ondalık hane sayısı korunur. */
export function parseNumberText(text: string | null | undefined): { value: number; decimals: number } | null {
  let t = (text ?? "").replace(/TL|₺|\s/gi, "");
  if (!t) return null;
  const negative = t.startsWith("-");
  if (negative) t = t.slice(1);
  if (t.includes(",")) {
    t = t.replace(/\./g, "").replace(",", ".");
  } else if (t.includes(".")) {
    const parts = t.split(".");
    if (parts[parts.length - 1] === "") parts.pop();
    t = decimalDot(parts) && parts.length > 1 ? `${parts.slice(0, -1).join("")}.${parts[parts.length - 1]}` : parts.join("");
  }
  if (!/^\d+(\.\d+)?$/.test(t)) return null;
  const value = Number(t);
  return { value: negative && value !== 0 ? -value : value, decimals: t.split(".")[1]?.length ?? 0 };
}

/**
 * Yazarken gösterim: süzer ve binlikleri noktayla gruplar ("32500" → "32.500", "1500,5" → "1.500,5"). Virgülsüz metinde son
 * noktadan sonra 1-2 rakam varsa kullanıcı henüz yazıyor olabilir ("32.5" → ondalık mı, "32.500"ün yarısı mı?); metin olduğu
 * gibi kalır, odaktan çıkınca `formatNumberValue` ile netleşir.
 */
export function formatNumberText(raw: string, { kind = "decimal", allowNegative = false }: NumberTextOptions = {}): string {
  let t = raw.replace(/[^\d.,-]/g, "");
  const negative = allowNegative && t.startsWith("-");
  t = t.replace(/-/g, "");
  const sign = negative ? "-" : "";
  // Tam sayı alanları (gün, adet, dakika) küçük değerler: gruplanmaz, formlar Number() ile okur.
  if (kind === "integer") return sign + t.replace(/[.,]/g, "").replace(/^0+(?=\d)/, "");

  const comma = t.indexOf(",");
  if (comma >= 0) {
    const int = t.slice(0, comma).replace(/\./g, "");
    return `${sign}${int ? group(int) : "0"},${t.slice(comma + 1).replace(/[.,]/g, "")}`;
  }
  if (t.includes(".")) {
    const parts = t.split(".");
    const last = parts[parts.length - 1]!;
    // Bekleyen ondalık ("12.", "12.5", "1.250.50"): olduğu gibi.
    if (last.length <= 2 && parts.slice(1, -1).every((p) => p.length === 3)) return sign + (t.startsWith(".") ? `0${t}` : t);
  }
  return sign + group(t.replace(/\./g, ""));
}

/**
 * Odaktan çıkınca: değer okunur ve binlikli yazılır ("32.500" → "32.500,00" para alanında). `fixed` verilirse o kadar ondalık
 * (para: 2). Fazla ondalık varsa metin değişmez ki doğrulama hatası görünsün ("10,001").
 */
export function formatNumberValue(text: string, { kind = "decimal", fixed }: NumberTextOptions & { fixed?: number } = {}): string {
  if (kind === "integer") return text;
  const parsed = parseNumberText(text);
  if (!parsed) return text;
  const max = fixed ?? 3;
  if (parsed.decimals > max) return text;
  return new Intl.NumberFormat("tr-TR", {
    minimumFractionDigits: fixed ?? 0,
    maximumFractionDigits: max,
    useGrouping: true,
  }).format(parsed.value);
}
