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

describe("palet: siyah, beyaz, mavi, lacivert (docs/04-tasarim.md, karar 2026-10-17)", () => {
  it("marka renkleri sabit; siyah ve beyaz saf", () => {
    expect(token("brand")).toBe("#00b0ff");
    expect(token("link")).toBe("#006ea6");
    expect(token("navy-950")).toBe("#000a1d");
    expect(token("navy-800")).toBe("#00385a");
    expect(token("cyan")).toBe("#00fdff");
    expect(token("black")).toBe(BLACK);
    expect(token("white")).toBe(WHITE);
  });

  it("yeşil/sarı durum renkleri ve mavi ton dizisi yok", () => {
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
  it("mavi ve camgöbeği beyaz zeminde yazı için yetersiz; kural bunu yasaklıyor", () => {
    expect(contrast(token("brand"), WHITE)).toBeLessThan(AA_TEXT);
    expect(contrast(token("cyan"), WHITE)).toBeLessThan(AA_TEXT);
  });

  it("link mavisi beyaz ve açık gri zeminde yazı olarak okunur", () => {
    for (const bg of [WHITE, token("neutral-50"), token("neutral-100")])
      expect(contrast(token("link"), bg)).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it.each(["navy-950", "navy-800"])("%s zemininde beyaz, neutral-300, mavi ve camgöbeği yazı okunur", (bg) => {
    for (const fg of [WHITE, token("neutral-300"), token("brand"), token("cyan")])
      expect(contrast(fg, token(bg))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("mavi düğme + siyah yazı; lacivert ve link zemininde beyaz yazı", () => {
    expect(contrast(BLACK, token("brand"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(WHITE, token("navy-950"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(WHITE, token("link"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("şeritler ve ikonlar beyaz zeminde: link ve lacivertler 3:1", () => {
    for (const c of ["link", "navy-800", "navy-950"]) expect(contrast(token(c), WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT);
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

  it("input kenarlığı beyaz zeminde en az 3:1", () => {
    expect(contrast(token("neutral-400"), WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT);
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
  it("zemin beyaz, yazı siyah, ana düğme mavi + siyah yazı, link mavisi, odak halkası lacivert", () => {
    expect(light("background")).toBe(WHITE);
    expect(light("foreground")).toBe(BLACK);
    expect(light("primary")).toBe(token("brand"));
    expect(light("primary-foreground")).toBe(BLACK);
    expect(light("link")).toBe(token("link"));
    expect(light("ring")).toBe(token("navy-950"));
  });

  it("odak halkası beyaz zeminde ve mavi düğmenin yanında en az 3:1", () => {
    expect(contrast(light("ring"), WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(light("ring"), light("primary"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });

  it.each(["foreground", "muted-foreground", "secondary-foreground", "accent-foreground", "link"])(
    "%s beyaz ve muted zeminde okunur",
    (name) => {
      expect(contrast(light(name), light("background"))).toBeGreaterThanOrEqual(AA_TEXT);
      expect(contrast(light(name), light("muted"))).toBeGreaterThanOrEqual(AA_TEXT);
    },
  );

  it("input kenarlığı 3:1", () => {
    expect(contrast(light("input"), light("background"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });

  it("lacivert #00385a ve camgöbeği beyaz kapsamın zemin/yazı değişkenlerinde yok (sadece koyu menüde)", () => {
    const body = block(":root")
      .split("\n")
      .filter((l) => !l.includes("--sidebar"))
      .join("\n");
    expect(body).not.toMatch(/navy-800|--color-cyan/);
  });
});

describe("koyu kapsam (.dark): üst çubuk, sitedeki koyu bölümler", () => {
  const surfaces = ["background", "card", "popover", "muted"] as const;

  it("zemin koyu lacivert, birincil renk mavi + siyah yazı, link mavi, odak halkası mavi", () => {
    expect(dark("background")).toBe(token("navy-950"));
    expect(dark("primary")).toBe(token("brand"));
    expect(dark("primary-foreground")).toBe(BLACK);
    expect(dark("link")).toBe(token("brand"));
    expect(dark("ring")).toBe(token("brand"));
  });

  it.each(surfaces)("ana metin, ikincil metin ve mavi link %s üstünde okunur", (s) => {
    expect(contrast(dark("foreground"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(dark("muted-foreground"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(dark("link"), dark(s))).toBeGreaterThanOrEqual(AA_TEXT);
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

  it("hata yazısı koyu zeminde okunmaz; hata mesajı açık kutuda gösterilir", () => {
    expect(contrast(token("danger-600"), dark("background"))).toBeLessThan(AA_TEXT);
  });
});

describe("panel sol menü (koyu lacivert, aktif öğe mavi)", () => {
  it("menü yazısı ve grup başlıkları koyu zeminde okunur", () => {
    expect(light("sidebar")).toBe(token("navy-950"));
    expect(contrast(light("sidebar-foreground"), light("sidebar"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token("neutral-400"), light("sidebar"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("aktif öğe: mavi zemin + siyah yazı; hover: lacivert + beyaz yazı", () => {
    expect(light("sidebar-primary")).toBe(token("brand"));
    expect(contrast(light("sidebar-primary-foreground"), light("sidebar-primary"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(light("sidebar-accent-foreground"), light("sidebar-accent"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("odak halkası menüde en az 3:1", () => {
    expect(contrast(light("sidebar-ring"), light("sidebar"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(light("sidebar-ring"), light("sidebar-accent"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
  });
});

describe("tanıtım sitesi renkleri (2026-10-21)", () => {
  const light = ["white", "mist", "cream"].map((n) => (n === "white" ? WHITE : token(n)));

  it("marka mavisi ana renk; mavi düğmede mürekkep yazı okunur", () => {
    expect(token("brand")).toBe("#00b0ff");
    expect(contrast(token("ink"), token("brand"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("mürekkep, ikincil yazı, link ve mercan yazısı beyaz, sis ve kremde okunur", () => {
    for (const bg of light)
      for (const fg of ["ink", "slate", "link", "coral-text"]) expect(contrast(token(fg), bg)).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("mercan beyazda yazı olamaz (sadece şerit/zemin); mercan rozetinde mürekkep yazı okunur", () => {
    expect(contrast(token("coral"), WHITE)).toBeLessThan(AA_TEXT);
    expect(contrast(token("ink"), token("coral-soft"))).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrast(token("ink"), token("coral"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("mürekkep bantta beyaz, slate-300, mavi ve mercan yazı okunur", () => {
    for (const fg of [WHITE, token("slate-300"), token("brand"), token("coral")])
      expect(contrast(fg, token("ink"))).toBeGreaterThanOrEqual(AA_TEXT);
  });
});

describe("defter rengi (mürekkep #0a1f3c; karar 2026-10-21)", () => {
  it("defter rengi sabit; ana sayfa başlığı beyazda AAA düzeyinde okunur", () => {
    expect(token("ink")).toBe("#0a1f3c");
    expect(contrast(token("ink"), WHITE)).toBeGreaterThanOrEqual(7);
  });

  it("defter zemininde (giriş/kayıt paneli, Telefondan yönet, İşin listede yok mu?) yazı, etiket ve link okunur", () => {
    for (const fg of [WHITE, token("slate-300"), token("brand")])
      expect(contrast(fg, token("ink"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("defter zeminindeki ikon kutusu: mavi kutu zeminden ayrışır, içindeki mürekkep ikon okunur", () => {
    expect(contrast(token("brand"), token("ink"))).toBeGreaterThanOrEqual(AA_NON_TEXT);
    expect(contrast(token("ink"), token("brand"))).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it("link mavisi ve mercan yazısı defter zemininde kullanılmaz (yetersiz)", () => {
    expect(contrast(token("link"), token("ink"))).toBeLessThan(AA_TEXT);
    expect(contrast(token("coral-text"), token("ink"))).toBeLessThan(AA_TEXT);
  });
});
