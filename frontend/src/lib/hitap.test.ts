import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

/**
 * Hitap (karar 2026-10-21): panel, giriş/kayıt, kurulum, müşteri sayfaları ve e-postalar "siz"; sadece tanıtım sitesi "sen"
 * olabilir. Kaynakta kesin "sen" kalıpları aranır (yorum satırları hariç). Site dosyaları ve site metinleri kapsam dışı.
 */
const root = path.resolve(__dirname, "../..");
const sources = [path.join(root, "src"), path.resolve(root, "../backend/src")];
const siteOnly = /[\\/](\(site\)|components[\\/]site|content[\\/](site|help|plans|legal)\.ts|app[\\/]not-found\.tsx|Migrations)[\\/]?|\.test\.tsx?$|schema\.d\.ts$/;
// JS'te \b sadece ASCII harfleri tanır (ı, ş… sınır sayılır); kelime sınırı harf olmayan komşuyla aranır.
const second = [
  /\p{L}+(abilirsin|ebilirsin|amazsın|emezsin|malısın|melisin)(?!\p{L})/u,
  /(?<!\p{L})(seni|senin|sana)(?!\p{L})/u,
  // Üçüncü tekil iyelikle karışmayanlar ("banka hesabını", "verilerin işlenmesi" sayılmaz).
  /(?<!\p{L})(şifren|şifreni|şifrenle|e-postanı|e-postan|işletmeni)(?!\p{L})/u,
];

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = path.join(dir, name);
    if (/^(node_modules|bin|obj|\.next.*)$/.test(name)) return [];
    if (statSync(full).isDirectory()) return files(full);
    return /\.(tsx?|cs)$/.test(name) && !siteOnly.test(full) ? [full] : [];
  });
}

describe("hitap", () => {
  it("panel ve e-posta metinlerinde 'sen' kalıbı yok", () => {
    const hits: string[] = [];
    for (const file of sources.flatMap(files)) {
      readFileSync(file, "utf8")
        .split("\n")
        .forEach((line, i) => {
          const text = line.trim();
          if (/^(\/\/|\*|\/\*)/.test(text)) return;
          if (second.some((r) => r.test(text))) hits.push(`${path.relative(root, file)}:${i + 1}: ${text.slice(0, 120)}`);
        });
    }
    expect(hits).toEqual([]);
  });
});
