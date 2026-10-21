import { mkdirSync } from "node:fs";
import path from "node:path";

import { expect, test, type Page } from "@playwright/test";
import sharp from "sharp";

import { frontendDir } from "../scripts/env.mjs";
import { platformAdmin } from "../support/api";
import { istanbulDay } from "../support/finance";

import { seedIsilti } from "./seed";

/**
 * Tanıtım sitesi önizlemesi (`npx playwright test -c playwright.demo.config.ts site-onizleme`): bütün site sayfaları masaüstü
 * 1440 px (2x) ve 1366 px (1x), telefon 375 px (2x) tam sayfa görüntü → test-results/site-onizleme/ (fotoğrafların netliğini
 * yakından incelemek için, repoya girmez). Ana sayfa, Özellikler, Fiyatlar, Giriş; panelin Bugün, takvim, müşteriler, hızlı satış,
 * ayarlar ekranları ve telefonda açık Menü (Işıltı Kuaför); /platform ve online randevu sayfası ayrıca docs/tasarim-onizleme/
 * altına yazılır (WebP; masaüstü 1440, telefon 750 px genişliğe küçültülür). Logo, koyu ton ve panel düzeni bunlardan incelenir.
 */
const out = path.join(frontendDir, "test-results", "site-onizleme");
const docs = path.resolve(frontendDir, "..", "docs", "tasarim-onizleme");
const pages: [string, string][] = [
  ["/", "ana-sayfa"],
  ["/ozellikler", "ozellikler"],
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
  ["/olmayan-sayfa", "404"],
];
const toDocs = new Set(["ana-sayfa", "ozellikler", "fiyatlar", "giris"]);
const devices = {
  "masaustu-1440": { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 },
  "masaustu-1366": { viewport: { width: 1366, height: 768 }, deviceScaleFactor: 1 },
  "telefon-375": { viewport: { width: 375, height: 812 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
} as const;
type Device = keyof typeof devices;

/** Tam sayfa ve ilk ekran görüntüsü; docs'a gidecekse küçültülmüş kopyası. */
async function capture(page: Page, device: Device, name: string, docsName?: string, tall = false) {
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
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
  mkdirSync(path.join(out, device), { recursive: true });
  const first = await page.screenshot();
  // Panel: sabit sol menü ve alt çubuk tam sayfa görüntüde ortada kalmasın diye pencere sayfa boyuna uzatılır.
  if (tall) {
    const height = await page.evaluate(() => document.documentElement.scrollHeight);
    await page.setViewportSize({ width: devices[device].viewport.width, height });
    await page.waitForTimeout(400);
  }
  // WebP en fazla 16383 px; uzun sayfalar oranı korunarak küçültülür.
  const shot = await page.screenshot({ fullPage: !tall });
  const { height = 0 } = await sharp(shot).metadata();
  const full = height > 16000 ? await sharp(shot).resize({ height: 16000 }).toBuffer() : shot;
  await sharp(full).webp({ quality: 85 }).toFile(path.join(out, device, `${name}.webp`));
  await sharp(first).webp({ quality: 90 }).toFile(path.join(out, device, `${name}-ilk-ekran.webp`));
  if (docsName && device !== "masaustu-1366") {
    mkdirSync(docs, { recursive: true });
    const label = device === "telefon-375" ? "telefon" : "masaustu";
    const width = device === "telefon-375" ? 750 : 1440;
    const resized = await sharp(shot).resize({ width }).toBuffer();
    const { height: h = 0 } = await sharp(resized).metadata();
    await sharp(h > 16000 ? await sharp(resized).resize({ height: 16000 }).toBuffer() : resized)
      .webp({ quality: 80 })
      .toFile(path.join(docs, `${docsName}-${label}.webp`));
  }
}

test("site önizlemesi: bütün sayfalar 1440 (2x), 1366 ve 375 px (2x); panel, platform, online randevu", async ({ browser }) => {
  test.setTimeout(900_000);
  for (const [device, options] of Object.entries(devices) as [Device, (typeof devices)[Device]][]) {
    const context = await browser.newContext({ ...options, locale: "tr-TR", timezoneId: "Europe/Istanbul" });
    const page = await context.newPage();
    for (const [url, name] of pages) {
      await page.goto(url);
      // Telefonda Chromium'un tam sayfa görüntüsü ilk ekran fotoğrafını boş bırakıyor (sayfada görünür); pencere uzatılır.
      await capture(page, device, name, toDocs.has(name) ? name : undefined, device === "telefon-375");
      if (device === "telefon-375") await page.setViewportSize(options.viewport);
    }
    await context.close();
  }

  // Panel ekranları: demo işletme, gün ortası.
  const { owner, slug, today } = await seedIsilti();
  const panel: [string, string, (page: Page) => Promise<void>][] = [
    ["/panel", "panel-bugun", (page) => expect(page.getByRole("region", { name: "Hızlı işlemler" })).toBeVisible({ timeout: 15_000 })],
    [`/panel/randevular?tarih=${today}`, "panel-takvim", (page) => expect(page.getByRole("heading", { level: 1 })).toBeVisible()],
    ["/panel/musteriler", "panel-musteriler", (page) => expect(page.getByRole("heading", { level: 1 })).toBeVisible()],
    ["/panel/satislar/yeni", "panel-hizli-satis", (page) => expect(page.getByRole("heading", { level: 1 })).toBeVisible()],
    ["/panel/ayarlar", "panel-ayarlar", (page) => expect(page.getByRole("heading", { level: 1 })).toBeVisible()],
  ];
  for (const device of ["masaustu-1440", "telefon-375"] as const) {
    const context = await owner.browserContext(browser, { ...devices[device], locale: "tr-TR", timezoneId: "Europe/Istanbul" });
    const page = await context.newPage();
    await page.clock.setFixedTime(new Date(`${istanbulDay()}T11:40:00+03:00`));
    for (const [url, name, ready] of panel) {
      await page.goto(url);
      await ready(page);
      await capture(page, device, name, name, true);
      await page.setViewportSize(devices[device].viewport);
    }
    if (device === "telefon-375") {
      await page.goto("/panel");
      await page.getByRole("button", { name: "Menü" }).click();
      await expect(page.getByRole("dialog")).toBeVisible();
      await page.waitForTimeout(500);
      await capture(page, device, "panel-menu", "panel-menu");
    }
    await context.close();
  }

  // Süper admin (/platform) ve online randevu sayfası (müşteriye açık).
  const admin = await platformAdmin();
  for (const device of ["masaustu-1440", "telefon-375"] as const) {
    const context = await admin.browserContext(browser, { ...devices[device], locale: "tr-TR", timezoneId: "Europe/Istanbul" });
    const page = await context.newPage();
    await page.goto("/platform");
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible({ timeout: 15_000 });
    await capture(page, device, "platform", "platform", device === "telefon-375");
    await context.close();

    const pub = await browser.newContext({ ...devices[device], locale: "tr-TR", timezoneId: "Europe/Istanbul" });
    const booking = await pub.newPage();
    await booking.goto(`/r/${slug}`);
    await expect(booking.getByRole("heading", { level: 1 })).toBeVisible({ timeout: 15_000 });
    await capture(booking, device, "online-randevu", "online-randevu", device === "telefon-375");
    await pub.close();
  }
});
