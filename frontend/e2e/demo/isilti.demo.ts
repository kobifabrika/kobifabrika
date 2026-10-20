import { mkdirSync } from "node:fs";
import path from "node:path";

import { expect, test, type Browser, type Page } from "@playwright/test";
import sharp from "sharp";

import { frontendDir } from "../scripts/env.mjs";
import { register, type ApiUser } from "../support/api";
import { istanbulDay } from "../support/finance";

import { seedIsilti } from "./seed";

/**
 * "Işıltı Kuaför" demo işletmesi ve ekran görüntüleri. Bütün veriler uydurmadır: adlar yaygın Türkçe adlardan, telefonlar
 * +90 5xx test aralığından rastgele (E2E yardımcısı uniquePhone). Görüntüler WebP: masaüstü 1280×800 (1x), telefon 375×812 (2x).
 */
const site = path.join(frontendDir, "public", "site");
const help = path.join(frontendDir, "public", "yardim");

const desktop = { viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 };
const phone = { viewport: { width: 375, height: 812 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true };

async function shot(page: Page, file: string) {
  await page.waitForLoadState("networkidle").catch(() => undefined);
  await expect(page.getByRole("status").filter({ hasText: /Yükleniyor/ })).toHaveCount(0, { timeout: 15_000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400); // giriş animasyonları
  mkdirSync(path.dirname(file), { recursive: true });
  await sharp(await page.screenshot()).webp({ quality: 82 }).toFile(file);
}

async function open(browser: Browser, user: ApiUser | null, device: typeof desktop | typeof phone) {
  const options = { ...device, locale: "tr-TR", timezoneId: "Europe/Istanbul" };
  const context = user ? await user.browserContext(browser, options) : await browser.newContext(options);
  const page = await context.newPage();
  // Selamlama ve "şimdi" çizgisi gün ortasını göstersin (betik hangi saatte koşarsa koşsun); veriler gerçek saate göre.
  await page.clock.setFixedTime(new Date(`${istanbulDay()}T11:40:00+03:00`));
  return page;
}

test("Işıltı Kuaför: demo verisi ve ekran görüntüleri", async ({ browser }) => {
  const { owner, slug: bookingSlug, today } = await seedIsilti();
  // --- Tanıtım sitesi: masaüstü ---
  const d = await open(browser, owner, desktop);
  await d.goto("/panel");
  await shot(d, path.join(site, "bugun-masaustu.webp"));
  await d.goto(`/panel/randevular?tarih=${today}`);
  await shot(d, path.join(site, "takvim-masaustu.webp"));
  await d.goto("/panel/cari-kasa?sekme=vadeler");
  await shot(d, path.join(site, "cari-masaustu.webp"));
  await d.goto("/panel/personel");
  await shot(d, path.join(site, "personel-masaustu.webp"));
  await d.context().close();

  // --- Telefon: site ve yardım ---
  const p = await open(browser, owner, phone);
  await p.goto("/panel");
  await shot(p, path.join(site, "bugun-telefon.webp"));
  await p.getByRole("region", { name: /Yarının randevuları/ }).scrollIntoViewIfNeeded();
  await shot(p, path.join(help, "whatsapp-hatirlatma.webp"));
  await p.goto("/panel/ayarlar/randevu");
  await p.getByText("Randevu linkiniz").scrollIntoViewIfNeeded();
  await shot(p, path.join(help, "online-randevu-linki.webp"));
  await p.goto("/panel");
  await p.getByRole("banner").getByRole("button", { name: "Menü" }).click();
  await shot(p, path.join(help, "telefonda-kullanim.webp"));
  await p.context().close();

  const pub = await open(browser, null, phone);
  await pub.goto(`/r/${bookingSlug}`);
  await shot(pub, path.join(site, "online-randevu-telefon.webp"));
  await pub.context().close();

  // İlk kurulum: yeni hesabın sihirbazı.
  const fresh = await register({ fullName: "Ayşe Işık" });
  const k = await open(browser, fresh, phone);
  await k.goto("/kurulum");
  await shot(k, path.join(help, "ilk-kurulum.webp"));
  await k.context().close();
});
