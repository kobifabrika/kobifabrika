import { mkdirSync } from "node:fs";
import path from "node:path";

import { test } from "@playwright/test";
import sharp from "sharp";

import { frontendDir } from "../scripts/env.mjs";

/**
 * Tanıtım sitesi önizlemesi (`npx playwright test -c playwright.demo.config.ts site-onizleme`): bütün site sayfaları masaüstü
 * 1440 ve 1366 px, telefon 375 px (2x) tam sayfa görüntü → test-results/site-onizleme/ (incelemek için, repoya girmez). Ana sayfa
 * ve Fiyatlar ayrıca docs/tasarim-onizleme/ altına (WebP) yazılır.
 */
const out = path.join(frontendDir, "test-results", "site-onizleme");
const docs = path.resolve(frontendDir, "..", "docs", "tasarim-onizleme");
const pages: [string, string][] = [
  ["/", "ana-sayfa"],
  ["/fiyatlar", "fiyatlar"],
  ["/sss", "sss"],
  ["/hakkimizda", "hakkimizda"],
  ["/iletisim", "iletisim"],
  ["/yardim", "yardim"],
  ["/yardim/ilk-kurulum", "yardim-makale"],
  ["/kvkk", "kvkk"],
  ["/kullanim-kosullari", "kullanim-kosullari"],
  ["/giris", "giris"],
  ["/kayit", "kayit"],
  ["/sifremi-unuttum", "sifremi-unuttum"],
];
const devices = {
  "masaustu-1440": { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 },
  "masaustu-1366": { viewport: { width: 1366, height: 768 }, deviceScaleFactor: 1 },
  "telefon-375": { viewport: { width: 375, height: 812 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
} as const;

test("site önizlemesi: bütün sayfalar 1440, 1366 ve 375 px", async ({ browser }) => {
  test.setTimeout(600_000);
  for (const [device, options] of Object.entries(devices)) {
    const context = await browser.newContext({ ...options, locale: "tr-TR", timezoneId: "Europe/Istanbul" });
    const page = await context.newPage();
    for (const [url, name] of pages) {
      await page.goto(url);
      await page.waitForLoadState("networkidle").catch(() => undefined);
      // Tembel görseller yüklensin.
      await page.evaluate(async () => {
        for (let y = 0; y < document.body.scrollHeight; y += 500) {
          window.scrollTo(0, y);
          await new Promise((r) => setTimeout(r, 80));
        }
        window.scrollTo(0, 0);
      });
      await page.waitForLoadState("networkidle").catch(() => undefined);
      await page.waitForTimeout(400);
      mkdirSync(path.join(out, device), { recursive: true });
      // WebP en fazla 16383 px; uzun telefon sayfaları oranı korunarak küçültülür.
      const shot = await page.screenshot({ fullPage: true });
      const { height = 0 } = await sharp(shot).metadata();
      const full = height > 16000 ? await sharp(shot).resize({ height: 16000 }).toBuffer() : shot;
      await sharp(full).webp({ quality: 80 }).toFile(path.join(out, device, `${name}.webp`));
      await sharp(await page.screenshot()).webp({ quality: 80 }).toFile(path.join(out, device, `${name}-ilk-ekran.webp`));
      if ((name === "ana-sayfa" || name === "fiyatlar") && device !== "masaustu-1366") {
        mkdirSync(docs, { recursive: true });
        const label = device === "telefon-375" ? "telefon" : "masaustu";
        await sharp(full).webp({ quality: 80 }).toFile(path.join(docs, `${name}-${label}.webp`));
      }
    }
    await context.close();
  }
});
