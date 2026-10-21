import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

import AxeBuilder from "@axe-core/playwright";
import { test, type Browser, type Page } from "@playwright/test";

import { frontendDir, mailpitUrl } from "../scripts/env.mjs";
import { addMember, platformAdmin, type ApiUser } from "../support/api";
import { istanbulDay } from "../support/finance";

import { seedIsilti } from "./seed";

/**
 * A10 rol turu (`npx playwright test -c playwright.demo.config.ts tur`): "Işıltı Kuaför" verisiyle her rol (Sahip, Yönetici,
 * Personel, Mali Müşavir, Süper admin) her ekranı masaüstünde (1280) ve telefonda (375) açar; tam sayfa görüntü
 * test-results/tur/<rol>-<cihaz>/ altına, otomatik bulgular (375 px yatay taşma, axe ciddi/kritik, 4xx/5xx, konsol hatası)
 * test-results/tur/bulgular.json'a yazılır. Görüntüleri insan gözü inceler; sonuç docs/pilot-test-raporu.md.
 */
const out = path.join(frontendDir, "test-results", "tur");
const devices = {
  masaustu: { viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 },
  telefon: { viewport: { width: 375, height: 812 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
} as const;

type Finding = { role: string; device: string; screen: string; url: string; issue: string };
const findings: Finding[] = [];

async function tour(browser: Browser, role: string, user: ApiUser | null, screens: [string, string][]) {
  for (const [device, options] of Object.entries(devices)) {
    const context = user
      ? await user.browserContext(browser, { ...options, locale: "tr-TR", timezoneId: "Europe/Istanbul" })
      : await browser.newContext({ ...options, locale: "tr-TR", timezoneId: "Europe/Istanbul" });
    const page = await context.newPage();
    await page.clock.setFixedTime(new Date(`${istanbulDay()}T11:40:00+03:00`));
    let current = { screen: "", url: "" };
    const note = (issue: string) => findings.push({ role, device, ...current, issue });
    page.on("console", (m) => m.type() === "error" && !/Failed to load resource/.test(m.text()) && note(`Konsol: ${m.text().slice(0, 200)}`));
    page.on("response", (r) => r.status() >= 500 && note(`HTTP ${r.status()} ${r.url()}`));
    for (const [i, [url, screen]] of screens.entries()) {
      current = { screen, url };
      await page.goto(url);
      await settle(page);
      if (device === "telefon") {
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        if (overflow > 0) note(`375 px'te ${overflow} px yatay taşma`);
      }
      const axe = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).exclude("[data-sonner-toaster]").analyze();
      for (const v of axe.violations.filter((v) => v.impact === "serious" || v.impact === "critical"))
        note(`axe ${v.id}: ${v.nodes.map((n) => n.target.join(" ")).slice(0, 2).join(" | ")}`);
      const file = path.join(out, `${role}-${device}`, `${String(i + 1).padStart(2, "0")}-${screen.replace(/[^\p{L}\p{N}]+/gu, "-")}.png`);
      mkdirSync(path.dirname(file), { recursive: true });
      await page.screenshot({ path: file, fullPage: true });
    }
    await context.close();
  }
  // Her rolden sonra yazılır: tur yarıda kalsa da o ana kadarki bulgular kalır.
  mkdirSync(out, { recursive: true });
  writeFileSync(path.join(out, "bulgular.json"), JSON.stringify(findings, null, 2));
}

async function settle(page: Page) {
  await page.waitForLoadState("networkidle").catch(() => undefined);
  await page
    .getByRole("status")
    .filter({ hasText: /Yükleniyor/ })
    .first()
    .waitFor({ state: "detached", timeout: 15_000 })
    .catch(() => undefined);
  await page.waitForTimeout(300);
}

test("rol turu: her rol, her ekran, masaüstü ve 375 px", async ({ browser }) => {
  test.setTimeout(3_600_000);
  const { owner, staff, contacts, slug, today } = await seedIsilti();
  const manager = await addMember(owner, "Manager", "Can Yönetici");
  const staffUser = await addMember(owner, "Staff", "Elif Kaya");
  const accountant = await addMember(owner, "Accountant", "Murat Müşavir");
  // Personel hesabı takvimdeki Elif Kaya kartına bağlanır (kendi randevuları ve mesaisi).
  const me = await staffUser.get<{ user: { id: string } }>("/api/auth/me");
  const card = await owner.get<Record<string, unknown>>(`/api/staff/${staff[0]}`);
  await owner.put(`/api/staff/${staff[0]}`, { ...card, userId: me.user.id });

  const contact = contacts[30]!;
  const sales = await owner.get<{ items: { id: string }[] }>("/api/sales");
  const sale = sales.items[0]!.id;
  const panel: [string, string][] = [
    ["/panel", "Bugün"],
    [`/panel/randevular?tarih=${today}`, "Takvim gün"],
    [`/panel/randevular?gorunum=hafta&tarih=${today}`, "Takvim hafta"],
    ["/panel/satislar", "Satışlar"],
    ["/panel/satislar/yeni", "Hızlı satış"],
    [`/panel/satislar/${sale}`, "Satış detayı"],
    ["/panel/teklifler", "Teklifler"],
    ["/panel/musteriler", "Müşteriler"],
    [`/panel/musteriler/${contact}`, "Müşteri kartı"],
    [`/panel/musteriler/${contact}?sekme=cari`, "Müşteri cari"],
    ["/panel/musteriler/yeni", "Yeni müşteri"],
    ["/panel/urunler", "Ürünler hizmetler"],
    ["/panel/stok", "Stok"],
    ["/panel/cari-kasa", "Cari bakiyeler"],
    ["/panel/cari-kasa?sekme=vadeler", "Vadeler"],
    ["/panel/cari-kasa?sekme=kasa", "Kasa banka"],
    ["/panel/cari-kasa?sekme=gelir-gider", "Gelir gider"],
    ["/panel/kampanyalar", "Kampanyalar"],
    ["/panel/kampanyalar?sekme=otomasyonlar", "Otomasyonlar"],
    ["/panel/personel", "Personel"],
    [`/panel/personel/${staff[0]}`, "Personel kartı"],
    ["/panel/mesai-izin", "Mesai"],
    ["/panel/mesai-izin?sekme=izinler", "İzinler"],
    ["/panel/raporlar", "Raporlar"],
    ["/panel/ayarlar", "Ayarlar işletme"],
    ["/panel/ayarlar/randevu", "Ayarlar randevu"],
    ["/panel/ayarlar/ekip", "Ayarlar ekip"],
    ["/panel/ayarlar/aktivite", "Aktivite günlüğü"],
    ["/panel/hesabim", "Hesabım"],
    ["/panel/olmayan-ekran", "Panel 404"],
  ];
  await tour(browser, "sahip", owner, panel);
  await tour(browser, "yonetici", manager, panel);
  await tour(browser, "personel", staffUser, panel);
  await tour(browser, "mali-musavir", accountant, panel);

  const admin = await platformAdmin();
  const business = await owner.get<{ id: string }>("/api/business");
  await tour(browser, "super-admin", admin, [
    ["/platform", "Özet"],
    ["/platform/isletmeler", "İşletmeler"],
    [`/platform/isletmeler/${business.id}`, "İşletme özet"],
    [`/platform/isletmeler/${business.id}?sekme=kullanicilar`, "İşletme kullanıcılar"],
    [`/platform/isletmeler/${business.id}?sekme=kredi`, "İşletme kredi"],
    [`/platform/isletmeler/${business.id}?sekme=notlar`, "İşletme notlar"],
    ["/platform/geri-bildirim", "Geri bildirim"],
  ]);

  // Müşterinin gördükleri: online randevu, aydınlatma, site, hata sayfaları.
  await tour(browser, "musteri", null, [
    [`/r/${slug}`, "Online randevu"],
    [`/r/${slug}/aydinlatma`, "Aydınlatma"],
    [`/r/olmayan-isletme`, "Online randevu bulunamadı"],
    ["/", "Site ana sayfa"],
    ["/fiyatlar", "Site fiyatlar"],
    ["/kayit", "Kayıt"],
    ["/giris", "Giriş"],
    ["/olmayan-sayfa", "Site 404"],
  ]);

  // Müşteriye giden son e-postalar (Mailpit HTML önizlemesi, telefon genişliğinde).
  const context = await browser.newContext({ ...devices.telefon });
  const mail = await context.newPage();
  const list = (await (await mail.request.get(`${mailpitUrl}/api/v1/messages?limit=20`)).json()) as { messages: { ID: string; Subject: string }[] };
  for (const [i, m] of list.messages.slice(0, 8).entries()) {
    await mail.goto(`${mailpitUrl}/view/${m.ID}.html`);
    const file = path.join(out, "eposta-telefon", `${String(i + 1).padStart(2, "0")}-${m.Subject.replace(/[^\p{L}\p{N}]+/gu, "-").slice(0, 60)}.png`);
    mkdirSync(path.dirname(file), { recursive: true });
    await mail.screenshot({ path: file, fullPage: true });
  }
  await context.close();

  writeFileSync(path.join(out, "bulgular.json"), JSON.stringify(findings, null, 2));
  console.log(`Tur bitti: ${findings.length} otomatik bulgu → test-results/tur/bulgular.json`);
});
