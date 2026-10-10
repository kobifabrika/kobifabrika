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
const BLACK = "#000000";
const AA_TEXT = 4.5;
const AA_NON_TEXT = 3;

describe("palet: siyah, beyaz, vurgu mavisi (docs/04-tasarim.md)", () => {
  it("tek marka rengi #00b0ff; siyah ve beyaz saf", () => {
    expect(token("brand")).toBe("#00b0ff");
    expect(token("black")).toBe(BLACK);
    expect(token("white")).toBe(WHITE);
  });

  it("eski marka renkleri ve yeşil/sarı durum renkleri token'larda yok", () => {
    for (const hex of ["#000a1d", "#00385a", "#006ea6", "#00fdff"]) expect(css.toLowerCase()).not.toContain(hex);
    expect(css).not.toMatch(/--color-(success|warning|brand-\d+)/);
  });

  it("nötrler renksiz gri (R = G = B)", () => {
    for (const n of [50, 100, 200, 300, 400, 500, 600, 700, 800, 900]) {
      const hex = token(`neutral-${n}`);
      expect(hex.slice(1, 3)).toBe(hex.slice(3, 5));
      expect(hex.slice(3, 5)).toBe(hex.slice(5, 7));
    }
  });

  it("Tailwind'in hazır paleti kapalı", () => {
    expect(css).toMatch(/--color-\*:\s*initial/);
  });
});

describe("kontrast kuralları", () => {
  it("mavi beyaz zeminde yazı için yetersiz; kural bunu yasaklıyor", () => {
    expect(contrast(token("brand"), WHITE)).toBeLessThan(AA_TEXT);
  });

  it("mavi siyah zeminde yazı/link olarak okunur", () => {
    expect(contrast(token("brand"), BLACK)).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("mavi buton + siyah yazı; siyah buton + beyaz yazı", () => {
    expect(contrast(BLACK, token("brand"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(WHITE, BLACK)).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(["neutral-600", "neutral-700", "danger-600"])(
    "%s beyaz ve neutral-50/100 zeminde yazı olarak okunur",
    (name) => {
      for (const bg of [WHITE, token("neutral-50"), token("neutral-100")])
        expect(contrast(token(name), bg)).toBeGreaterThanOrEqual(AA_TEXT);
    },
  );

  it("hata yazısı kendi açık zemininde (danger-50) okunur", () => {
    expect(contrast(token("danger-600"), token("danger-50"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(["neutral-300", "neutral-400"])("%s siyah ve neutral-900 zeminde ikincil metin olarak okunur", (name) => {
    expect(contrast(token(name), BLACK)).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token(name), token("neutral-900"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("input kenarlığı beyaz zeminde en az 3:1", () => {
    expect(contrast(token("neutral-400"), WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });

  it("odak: mavi halka siyah buton/zemin yanında 3:1; beyaz zeminde input kenarı odakta siyaha döner", () => {
    expect(contrast(token("brand"), BLACK)).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(BLACK, WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });
});

/**
 * Kapsam blokları (:root, .dark): değişkenler var(--color-x) veya color-mix(in srgb, var(--color-a) N%, var(--color-b))
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

const light = (name: string) => variable(":root", name);
const dark = (name: string) => variable(".dark", name);

describe("beyaz kapsam: panel içeriği, giriş ekranları, site", () => {
  it("zemin beyaz, yazı siyah, ana buton siyah + beyaz yazı, odak halkası mavi", () => {
    expect(light("background")).toBe(WHITE);
    expect(light("foreground")).toBe(BLACK);
    expect(light("primary")).toBe(BLACK);
    expect(light("primary-foreground")).toBe(WHITE);
    expect(light("ring")).toBe(token("brand"));
  });

  it.each(["foreground", "muted-foreground", "secondary-foreground", "accent-foreground"])(
    "%s beyaz ve muted zeminde okunur",
    (name) => {
      expect(contrast(light(name), light("background"))).toBeGreaterThanOrEqual(AA_TEXT);
      expect(contrast(light(name), light("muted"))).toBeGreaterThanOrEqual(AA_TEXT);
    },
  );

  it("input kenarlığı 3:1", () => {
    expect(contrast(light("input"), light("background"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });
});

describe("siyah kapsam (.dark): üst çubuk, sitedeki siyah bölümler", () => {
  const surfaces = ["background", "card", "popover", "muted"] as const;

  it("zemin siyah, birincil renk mavi + siyah yazı, odak halkası mavi", () => {
    expect(dark("background")).toBe(BLACK);
    expect(dark("primary")).toBe(token("brand"));
    expect(dark("primary-foreground")).toBe(BLACK);
    expect(dark("ring")).toBe(token("brand"));
  });

  it.each(surfaces)("ana metin, ikincil metin ve mavi link %s üstünde okunur", (s) => {
    expect(contrast(dark("foreground"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(dark("muted-foreground"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(dark("primary"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("buton: mavi zemin + siyah yazı", () => {
    expect(contrast(dark("primary-foreground"), dark("primary"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(surfaces)("odak halkası %s üstünde en az 3:1", (s) => {
    expect(contrast(dark("ring"), dark(s))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });

  it("input kenarlığı kart ve zeminde en az 3:1", () => {
    expect(contrast(dark("input"), dark("card"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(dark("input"), dark("background"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });

  it("hata yazısı siyah zeminde okunmaz; hata mesajı açık kutuda gösterilir", () => {
    expect(contrast(token("danger-600"), BLACK)).toBeLessThan(AA_TEXT);
  });
});

describe("panel sol menü (siyah, aktif öğe mavi)", () => {
  it("menü yazısı ve grup başlıkları siyah zeminde okunur", () => {
    expect(contrast(light("sidebar-foreground"), light("sidebar"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token("neutral-400"), light("sidebar"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("aktif öğe: mavi zemin + siyah yazı; hover: koyu gri + beyaz yazı", () => {
    expect(light("sidebar-primary")).toBe(token("brand"));
    expect(contrast(light("sidebar-primary-foreground"), light("sidebar-primary"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(light("sidebar-accent-foreground"), light("sidebar-accent"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("odak halkası menüde en az 3:1", () => {
    expect(contrast(light("sidebar-ring"), light("sidebar"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(light("sidebar-ring"), light("sidebar-accent"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });
});
