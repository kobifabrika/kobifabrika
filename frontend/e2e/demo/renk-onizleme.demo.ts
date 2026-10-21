import { mkdirSync, readFileSync } from "node:fs";
import path from "node:path";

import { expect, test, type Browser, type Page } from "@playwright/test";
import sharp, { type OverlayOptions } from "sharp";

import { frontendDir } from "../scripts/env.mjs";
import type { ApiUser } from "../support/api";
import { istanbulDay } from "../support/finance";

import { seedIsilti } from "./seed";

/**
 * Renk yönü önizlemesi (2026-10-21, sadece öneri): `npx playwright test -c playwright.demo.config.ts renk-onizleme`.
 * Uygulama koduna dokunmaz: docs/tasarim-secenekleri/kaynak/tema-*.css sayfaya enjekte edilir (token'ların üstüne yazar).
 * Her seçenek (ve karşılaştırma için mevcut hali) için 4 ekran × masaüstü (1440×900) ve telefon (375×812, 2x):
 * site ana sayfa üst bölümü, fiyatlar, panel Bugün, online randevu. Çıktı docs/tasarim-secenekleri/<seçenek>/ ve
 * karsilastirma.webp; seçeneklerin açıklaması ve renk kodları docs/tasarim-secenekleri/README.md. Seçim yapılınca bu betik ve
 * kaynak/ klasörü silinebilir.
 */
const out = path.resolve(frontendDir, "..", "docs", "tasarim-secenekleri");
const themes = [
  { key: "mevcut", label: "Mevcut", css: null },
  { key: "A", label: "A · Gece mavisi + altın", css: "tema-a.css" },
  { key: "B", label: "B · Ferah beyaz + mercan", css: "tema-b.css" },
  { key: "C", label: "C · Krem + antrasit", css: "tema-c.css" },
] as const;
const devices = {
  masaustu: { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 },
  telefon: { viewport: { width: 375, height: 812 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
} as const;
const screens = [
  { key: "1-site-ana", label: "Site ana sayfa", auth: false },
  { key: "2-fiyatlar", label: "Fiyatlar", auth: false },
  { key: "3-panel-bugun", label: "Panel · Bugün", auth: true },
  { key: "4-online-randevu", label: "Online randevu", auth: false },
] as const;

async function open(browser: Browser, user: ApiUser | null, device: keyof typeof devices) {
  const options = { ...devices[device], locale: "tr-TR", timezoneId: "Europe/Istanbul" };
  const context = user ? await user.browserContext(browser, options) : await browser.newContext(options);
  const page = await context.newPage();
  await page.clock.setFixedTime(new Date(`${istanbulDay()}T11:40:00+03:00`));
  return page;
}

async function shoot(page: Page, url: string, css: string | null, file: string) {
  await page.goto(url);
  await page.waitForLoadState("networkidle").catch(() => undefined);
  await expect(page.getByRole("status").filter({ hasText: /Yükleniyor/ })).toHaveCount(0, { timeout: 15_000 });
  if (css) await page.addStyleTag({ content: css });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  mkdirSync(path.dirname(file), { recursive: true });
  await sharp(await page.screenshot()).webp({ quality: 86 }).toFile(file);
}

test("renk yönleri: A, B, C önizleme görüntüleri ve karşılaştırma", async ({ browser }) => {
  test.setTimeout(900_000);
  const { owner, slug } = await seedIsilti();
  const urls: Record<(typeof screens)[number]["key"], string> = {
    "1-site-ana": "/",
    "2-fiyatlar": "/fiyatlar",
    "3-panel-bugun": "/panel",
    "4-online-randevu": `/r/${slug}`,
  };

  for (const device of Object.keys(devices) as (keyof typeof devices)[]) {
    const pub = await open(browser, null, device);
    const panel = await open(browser, owner, device);
    for (const theme of themes) {
      const css = theme.css ? readFileSync(path.join(out, "kaynak", theme.css), "utf8") : null;
      for (const s of screens) {
        await shoot(s.auth ? panel : pub, urls[s.key], css, path.join(out, theme.key, `${s.key}-${device}.webp`));
      }
    }
    await pub.context().close();
    await panel.context().close();
  }

  // Tek karşılaştırma görseli: satırlar seçenekler, sütunlar 4 ekran (masaüstü) + 4 ekran (telefon).
  const dW = 576, dH = 360, pW = 166, gap = 16, labelW = 220, headH = 56, rowH = dH + 24;
  const width = labelW + screens.length * (dW + gap) + screens.length * (pW + gap) + gap;
  const height = headH + themes.length * rowH + gap;
  const layers: OverlayOptions[] = [];
  const text = (x: number, y: number, s: string, size: number, weight = 700) =>
    `<text x="${x}" y="${y}" font-family="Segoe UI, Arial, sans-serif" font-size="${size}" font-weight="${weight}" fill="#111">${s}</text>`;
  let svg = "";
  screens.forEach((s, i) => (svg += text(labelW + i * (dW + gap), 36, `${s.label} · masaüstü`, 18)));
  screens.forEach((s, i) => (svg += text(labelW + screens.length * (dW + gap) + i * (pW + gap), 36, `${i + 1} · telefon`, 16)));
  for (const [r, theme] of themes.entries()) {
    const y = headH + r * rowH;
    const [title, ...rest] = theme.label.split(" · ");
    svg += text(16, y + 40, title!, 30, 800);
    if (rest.length) svg += text(16, y + 72, rest.join(" · "), 17, 500);
    for (const [i, s] of screens.entries()) {
      const d = path.join(out, theme.key, `${s.key}-masaustu.webp`);
      const p = path.join(out, theme.key, `${s.key}-telefon.webp`);
      layers.push({ input: await sharp(d).resize(dW, dH).toBuffer(), left: labelW + i * (dW + gap), top: y });
      layers.push({
        input: await sharp(p).resize(pW, dH, { fit: "cover", position: "top" }).toBuffer(),
        left: labelW + screens.length * (dW + gap) + i * (pW + gap),
        top: y,
      });
    }
  }
  layers.push({ input: Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">${svg}</svg>`), left: 0, top: 0 });
  await sharp({ create: { width, height, channels: 3, background: "#ffffff" } })
    .composite(layers)
    .webp({ quality: 88 })
    .toFile(path.join(out, "karsilastirma.webp"));
});
