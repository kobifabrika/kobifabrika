import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

/**
 * docs/04-tasarim.md kontrast kurallarını globals.css'teki gerçek token değerleri üzerinden doğrular.
 * Bir token değeri değişir ve kural bozulursa bu test kırılır.
 */
const css = readFileSync(join(process.cwd(), "src/app/globals.css"), "utf8");

function token(name: string): string {
  const match = css.match(new RegExp(`--color-${name}:\\s*(#[0-9a-fA-F]{6})`));
  if (!match) throw new Error(`Token bulunamadı: --color-${name}`);
  return match[1];
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const WHITE = "#ffffff";
const AA_TEXT = 4.5;
const AA_NON_TEXT = 3;

describe("marka renkleri (docs/04-tasarim.md)", () => {
  it("5 marka rengi dokümandaki değerlerle aynı", () => {
    expect({
      900: token("brand-900"),
      700: token("brand-700"),
      500: token("brand-500"),
      300: token("brand-300"),
      100: token("brand-100"),
    }).toEqual({ 900: "#000a1d", 700: "#00385a", 500: "#006ea6", 300: "#00b0ff", 100: "#00fdff" });
  });

  it.each(["brand-900", "brand-700", "brand-500"])("%s beyaz zeminde yazı olarak okunur (≥ 4.5:1)", (name) => {
    expect(contrast(token(name), WHITE)).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(["brand-300", "brand-100"])("%s beyaz zeminde yazı için yetersiz; kural bunu yasaklıyor", (name) => {
    expect(contrast(token(name), WHITE)).toBeLessThan(AA_TEXT);
  });

  it.each(["brand-300", "brand-100"])("%s koyu zeminde (brand-900) okunur", (name) => {
    expect(contrast(token(name), token("brand-900"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("brand-500 buton + beyaz yazı ve brand-300 buton + brand-900 yazı okunur", () => {
    expect(contrast(token("brand-500"), WHITE)).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token("brand-300"), token("brand-900"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("odak halkası: açık zeminde brand-500, koyu zeminde brand-100 en az 3:1", () => {
    expect(css).toMatch(/--ring:\s*var\(--color-brand-500\)/);
    expect(css).toMatch(/--sidebar-ring:\s*var\(--color-brand-100\)/);
    expect(contrast(token("brand-500"), WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(token("brand-100"), token("brand-900"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });
});

describe("nötr ve durum renkleri", () => {
  it.each(["neutral-600", "success-600", "warning-600", "danger-600"])(
    "%s beyaz ve sayfa zemininde yazı olarak okunur",
    (name) => {
      expect(contrast(token(name), WHITE)).toBeGreaterThanOrEqual(AA_TEXT);
      expect(contrast(token(name), token("neutral-50"))).toBeGreaterThanOrEqual(AA_TEXT);
    },
  );

  it("Tailwind'in hazır paleti kapalı", () => {
    expect(css).toMatch(/--color-\*:\s*initial/);
  });
});
