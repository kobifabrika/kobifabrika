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

  it.each(["success", "warning", "danger"])("%s-600 kendi açık zemininde (%s-50) okunur (banner, rozet)", (name) => {
    expect(contrast(token(`${name}-600`), token(`${name}-50`))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("Tailwind'in hazır paleti kapalı", () => {
    expect(css).toMatch(/--color-\*:\s*initial/);
  });
});

/**
 * Koyu kapsam (.dark): değişkenler var(--color-x) veya color-mix(in srgb, var(--color-a) N%, var(--color-b))
 * olarak yazılır; burada gerçek hex'e çözülür.
 */
function block(selector: string): string {
  const start = css.indexOf(`\n${selector}`);
  const match = start < 0 ? null : css.slice(start).match(/\{([\s\S]*?)\n\}/);
  if (!match) throw new Error(`Blok bulunamadı: ${selector}`);
  return match[1];
}

function mix(a: string, b: string, weightA: number): string {
  const channel = (i: number) =>
    Math.round(parseInt(a.slice(i, i + 2), 16) * weightA + parseInt(b.slice(i, i + 2), 16) * (1 - weightA));
  return `#${[1, 3, 5].map((i) => channel(i).toString(16).padStart(2, "0")).join("")}`;
}

function resolve(value: string): string {
  const v = value.trim();
  const ref = v.match(/^var\(--color-([\w-]+)\)$/);
  if (ref) return token(ref[1]);
  const m = v.match(/^color-mix\(in srgb, var\(--color-([\w-]+)\) (\d+)%, var\(--color-([\w-]+)\)\)$/);
  if (m) return mix(token(m[1]), token(m[3]), Number(m[2]) / 100);
  throw new Error(`Çözülemeyen değer: ${v}`);
}

function variable(selector: string, name: string): string {
  const match = block(selector).match(new RegExp(`--${name}:\\s*([^;]+);`));
  if (!match) throw new Error(`Değişken bulunamadı: ${selector} --${name}`);
  return resolve(match[1]);
}

const dark = (name: string) => variable(".dark", name);

describe("koyu kapsam: site, kimlik ekranları, kurulum (docs/04-tasarim.md)", () => {
  const surfaces = ["background", "card", "popover", "muted"] as const;

  it("zemin brand-900, birincil renk brand-300 + brand-900 yazı, odak halkası brand-100", () => {
    expect(dark("background")).toBe(token("brand-900"));
    expect(dark("primary")).toBe(token("brand-300"));
    expect(dark("primary-foreground")).toBe(token("brand-900"));
    expect(dark("ring")).toBe(token("brand-100"));
  });

  it("yüzeyler koyu kalır (beyaz yazı en az 12:1)", () => {
    for (const s of surfaces) expect(contrast(dark(s), WHITE)).toBeGreaterThanOrEqual(12);
  });

  it.each(surfaces)("ana metin (foreground) %s üstünde okunur", (s) => {
    expect(contrast(dark("foreground"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(surfaces)("ikincil gri metin (muted-foreground, neutral-300) %s üstünde okunur", (s) => {
    expect(contrast(dark("muted-foreground"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token("neutral-300"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(surfaces)("link/vurgu (brand-300, brand-100) %s üstünde okunur", (s) => {
    expect(contrast(dark("primary"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token("brand-100"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("buton: brand-300 zemin + brand-900 yazı; hover brand-100 + brand-900", () => {
    expect(contrast(dark("primary-foreground"), dark("primary"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token("brand-900"), token("brand-100"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("ikincil ve vurgu yüzeyleri (brand-700) beyaz yazıyla okunur", () => {
    expect(contrast(dark("secondary-foreground"), dark("secondary"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(dark("accent-foreground"), dark("accent"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(surfaces)("odak halkası %s üstünde en az 3:1", (s) => {
    expect(contrast(dark("ring"), dark(s))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });

  it("input kenarlığı kart ve zeminde en az 3:1", () => {
    expect(contrast(dark("input"), dark("card"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(dark("input"), dark("background"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });

  it("koyu zeminde hata/başarı mesajları açık kutuda gösterilir (durum renkleri koyu zeminde yazı olamaz)", () => {
    expect(contrast(token("danger-600"), token("brand-900"))).toBeLessThan(AA_TEXT);
    expect(contrast(token("danger-600"), token("danger-50"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("brand-500 koyu zeminde yazı için yetersiz; koyu kapsamda link rengi brand-300", () => {
    expect(contrast(token("brand-500"), token("brand-900"))).toBeLessThan(AA_TEXT);
  });
});

describe("panel sol menü ve üst çubuk (koyu)", () => {
  const sidebar = (name: string) => variable(":root", name);

  it("menü yazısı, aktif öğe ve grup başlıkları okunur", () => {
    expect(contrast(sidebar("sidebar-foreground"), sidebar("sidebar"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(sidebar("sidebar-accent-foreground"), sidebar("sidebar-accent"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(sidebar("sidebar-primary"), sidebar("sidebar-accent"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(token("neutral-400"), sidebar("sidebar"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("odak halkası koyu menüde en az 3:1", () => {
    expect(contrast(sidebar("sidebar-ring"), sidebar("sidebar"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(sidebar("sidebar-ring"), sidebar("sidebar-accent"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });
});
