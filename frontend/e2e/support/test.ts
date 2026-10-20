import AxeBuilder from "@axe-core/playwright";
import { test as base, expect, type Browser, type BrowserContext, type Page, type TestInfo } from "@playwright/test";

import { ownerWithBusiness, type ApiUser, type Sector } from "./api";
import { isSmsProject, setSmsTarget, target } from "./target";

/**
 * Ortak test düzeneği. Her sayfada kendiliğinden:
 * - konsolda hata, yakalanmamış JS hatası, API'den veya frontend'den beklenmeyen 4xx/5xx yanıtı → test düşer
 *   (beklenen hata `guard.allow(404, /api\/contacts/)` ile işaretlenir);
 * - `check(page, "ad")` / `visit(page, url, "ad")`: 375 px'te yatay taşma ve axe-core ciddi/kritik erişilebilirlik hatası
 *   (renk kontrastı dahil) → test düşer; ekran görüntüsü rapora eklenir; sayfa 3 saniyeden geç açılırsa uyarı.
 */

const SLOW_MS = 3000;

type Allowed = { status: number | "network"; url: RegExp };

export class Guard {
  readonly issues: string[] = [];
  // Oturum yoklaması: oturumsuz ziyaretçide /api/auth/me 401 döner (tasarım gereği).
  private allowed: Allowed[] = [{ status: 401, url: /\/api\/auth\/me$/ }];

  constructor(private readonly info: TestInfo) {}

  /** Beklenen HTTP hatası (ör. başka işletmenin kaydı → 404). `network`: bağlantı hatası (API kapalı senaryosu). */
  allow(status: number | "network", url: RegExp = /.*/) {
    this.allowed.push({ status, url });
  }

  private isAllowed(status: number | "network", url: string) {
    return this.allowed.some((a) => a.status === status && a.url.test(url));
  }

  watch(page: Page, label = "sayfa") {
    page.on("console", (msg) => {
      if (msg.type() !== "error") return;
      const text = msg.text();
      // HTTP hataları aşağıda yanıt üzerinden (izin listesiyle) kontrol edilir; tarayıcı bunları ayrıca konsola yazar.
      if (
        /Failed to load resource|the server responded with a status of|net::ERR_|Fetch API cannot load|due to access control checks/i.test(
          text,
        )
      )
        return;
      this.issues.push(`[${label}] Konsolda hata: ${text}\n    ${page.url()}`);
    });
    page.on("pageerror", (error) => {
      // WebKit, sayfadan ayrılırken iptal edilen Next.js önyükleme (?_rsc=) isteğini "access control checks" hatası
      // olarak bildirir; kullanıcıya etkisi yoktur.
      if (/_rsc=.*due to access control checks/.test(error.message)) return;
      this.issues.push(`[${label}] JS hatası: ${error.message}\n    ${page.url()}`);
    });
    page.on("response", (response) => {
      const url = response.url();
      const { apiUrl, webUrl } = target();
      if (!url.startsWith(apiUrl) && !url.startsWith(webUrl)) return;
      const status = response.status();
      if (status < 400 || this.isAllowed(status, url)) return;
      this.issues.push(`[${label}] Beklenmeyen HTTP ${status}: ${response.request().method()} ${url}`);
    });
    page.on("requestfailed", (request) => {
      const url = request.url();
      if (!url.startsWith(target().apiUrl) || this.isAllowed("network", url)) return;
      const reason = request.failure()?.errorText ?? "";
      // Sayfa değişirken iptal edilen istekler hata değildir.
      if (/ERR_ABORTED|cancelled|NS_BINDING_ABORTED|Load request cancelled/i.test(reason)) return;
      this.issues.push(`[${label}] İstek başarısız: ${request.method()} ${url} (${reason})`);
    });
  }

  warn(text: string) {
    this.info.annotations.push({ type: "uyarı", description: text });
    console.warn(`UYARI: ${text}`);
  }
}

/** Yükleme göstergeleri kaybolana ve ağ durulana kadar bekler. */
export async function waitReady(page: Page) {
  await page.waitForLoadState("load");
  await page.waitForLoadState("networkidle", { timeout: 15_000 }).catch(() => undefined);
  await expect(page.getByRole("status").filter({ hasText: /Yükleniyor|hazırlanıyor/ })).toHaveCount(0, {
    timeout: 15_000,
  });
  // Açılan pencere/alt sayfanın giriş animasyonu bitsin: yarı saydam halde kontrast ölçülmesin (Sprint 6A; aralıklı axe hatası).
  // Sonsuz animasyonlar (yükleme dönmesi) beklenmez; toast'lar axe'den zaten hariç.
  await page
    .evaluate(() =>
      Promise.all(
        document
          .getAnimations()
          .filter((a) => a.effect?.getComputedTiming().iterations !== Infinity)
          .map((a) => a.finished.catch(() => undefined)),
      ),
    )
    .catch(() => undefined);
}

type Fixtures = {
  guard: Guard;
  /** Ekran kontrolü: taşma + axe + ekran görüntüsü (rapora). */
  check: (page: Page, name: string) => Promise<void>;
  /** Git + hazır olmasını bekle + süre ölç + ekran kontrolü. */
  visit: (page: Page, url: string, name: string) => Promise<void>;
  /** Kurulumu bitmiş işletmenin Sahibi (API ile) ve onun oturumuyla açılmış sayfa. */
  owner: ApiUser;
  ownerPage: Page;
  /** Oturumsuz ayrı bir tarayıcı bağlamı (müşterinin telefonu gibi); aynı otomatik kontrollerle. */
  newPublicPage: () => Promise<Page>;
  /** Verilen kullanıcının oturumuyla ayrı bir bağlamda sayfa; aynı otomatik kontrollerle. */
  pageAs: (user: ApiUser) => Promise<Page>;
};

type Options = { ownerSector: Sector; ownerConfirmed: boolean };

/** Ek bağlamlar projenin cihaz ayarlarıyla (telefon/masaüstü) açılır. */
const contextOptions = (_browser: Browser, info: TestInfo) => {
  const { viewport, screen, userAgent, deviceScaleFactor, isMobile, hasTouch, locale, timezoneId } = info.project.use;
  return { viewport, screen, userAgent, deviceScaleFactor, isMobile, hasTouch, locale, timezoneId, baseURL: target().webUrl };
};

export const test = base.extend<Fixtures & Options, { e2eTarget: void }>({
  // Worker başında hangi E2E çiftiyle (SMS kapalı / açık) konuşulacağı (support/target.ts).
  e2eTarget: [
    async ({}, use, workerInfo) => {
      setSmsTarget(isSmsProject(workerInfo.project.name));
      await use();
    },
    { scope: "worker", auto: true },
  ],
  ownerSector: ["Other", { option: true }],
  ownerConfirmed: [false, { option: true }],

  guard: async ({}, use, info) => {
    const guard = new Guard(info);
    await use(guard);
    if (guard.issues.length > 0 && info.status === info.expectedStatus) {
      throw new Error(`Otomatik kontroller ${guard.issues.length} sorun buldu:\n- ${guard.issues.join("\n- ")}`);
    }
  },

  page: async ({ page, guard }, use) => {
    guard.watch(page);
    await use(page);
  },

  check: async ({ guard }, use, info) => {
    let shots = 0;
    await use(async (page, name) => {
      await waitReady(page);
      const project = info.project.name;
      const viewport = page.viewportSize();
      if (viewport && viewport.width <= 400) {
        const overflow = await page.evaluate(() => {
          const root = document.documentElement;
          if (root.scrollWidth <= root.clientWidth) return null;
          // Taşan en geniş öğe (hata mesajı için).
          let widest = "";
          let max = root.clientWidth;
          for (const el of Array.from(document.body.querySelectorAll("*"))) {
            const right = el.getBoundingClientRect().right;
            if (right > max + 1) {
              max = right;
              widest = `${el.tagName.toLowerCase()}${el.id ? `#${el.id}` : ""}.${String(el.className).split(" ").slice(0, 3).join(".")}`;
            }
          }
          return { scroll: root.scrollWidth, client: root.clientWidth, widest };
        });
        if (overflow)
          guard.issues.push(
            `[${name}] ${viewport.width} px'te yatay taşma: scrollWidth ${overflow.scroll} > ${overflow.client} (${overflow.widest})`,
          );
      }

      const axe = await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
        // Bildirim (toast) yığını: arkadaki toast'lar ve giriş/çıkış animasyonu bilerek yarı saydam; kalıcı halde
        // siyah yazı beyaz zemin (components/ui/sonner.tsx).
        .exclude("[data-sonner-toaster]")
        .analyze();
      for (const v of axe.violations.filter((v) => v.impact === "serious" || v.impact === "critical")) {
        const nodes = v.nodes
          .slice(0, 3)
          .map(
            (n) =>
              `${n.target.join(" ")}${n.failureSummary ? ` → ${n.failureSummary.split("\n").slice(1, 2).join("").trim()}` : ""}`,
          )
          .join(" | ");
        guard.issues.push(`[${name}] Erişilebilirlik (${v.impact}) ${v.id}: ${v.help} — ${nodes}`);
      }

      // Dosyaya yazılıp yolu eklenir (JSON rapor şişmesin); HTML rapor dosyayı kendi klasörüne kopyalar.
      shots += 1;
      const file = info.outputPath(`ekran-${String(shots).padStart(2, "0")}.png`);
      await page.screenshot({ fullPage: true, path: file });
      await info.attach(`${name} (${project})`, { path: file, contentType: "image/png" });
    });
  },

  visit: async ({ check, guard }, use) => {
    await use(async (page, url, name) => {
      const started = Date.now();
      await page.goto(url);
      await waitReady(page);
      const ms = Date.now() - started;
      if (ms > SLOW_MS) guard.warn(`${name} (${url}) ${(ms / 1000).toFixed(1)} sn'de açıldı (sınır 3 sn).`);
      await check(page, name);
    });
  },

  owner: async ({ ownerSector, ownerConfirmed }, use) => {
    const owner = await ownerWithBusiness({ sector: ownerSector, confirmEmail: ownerConfirmed });
    await use(owner);
    await owner.dispose();
  },

  ownerPage: async ({ owner, pageAs }, use) => {
    await use(await pageAs(owner));
  },

  newPublicPage: async ({ browser, guard }, use, info) => {
    const contexts: BrowserContext[] = [];
    await use(async () => {
      const context = await browser.newContext(contextOptions(browser, info));
      contexts.push(context);
      const page = await context.newPage();
      guard.watch(page, "oturumsuz");
      return page;
    });
    for (const c of contexts) await c.close();
  },

  pageAs: async ({ browser, guard }, use, info) => {
    const contexts: BrowserContext[] = [];
    await use(async (user) => {
      const context = await user.browserContext(browser, contextOptions(browser, info));
      contexts.push(context);
      const page = await context.newPage();
      guard.watch(page, user.fullName);
      return page;
    });
    for (const c of contexts) await c.close();
  },
});

export { expect };

/** Masaüstü projesi mi (sol menü) yoksa telefon mu (alt çubuk). */
export const isDesktop = (info: TestInfo) => info.project.name === "masaustu";
