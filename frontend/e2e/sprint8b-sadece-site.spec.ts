import { expect, isDesktop, test } from "./support/test";
import { target } from "./support/target";

/**
 * Sprint 8B ara adım (karar 2026-10-21): sadece-site derlemesi (NEXT_PUBLIC_SITE_ONLY=1, API yok). Site sayfaları açılır; kayda ve
 * girişe giden her çağrı "Pilot listesine katıl" olup hazır mesajlı WhatsApp linkine gider; giriş, kayıt, panel ve link sayfaları
 * "Çok yakında" gösterir (yönlendirmeden, noindex); tarayıcı API'ye hiç istek atmaz; sitedeki iç bağlantıların hepsi açılır;
 * robots ve site haritası sadece site sayfaları, canonical kök alan.
 */
const sitePages = ["/", "/ozellikler", "/fiyatlar", "/sss", "/hakkimizda", "/iletisim", "/yardim", "/yardim/ilk-kurulum", "/kvkk"];
const appPages = ["/giris", "/kayit", "/panel", "/panel/musteriler", "/platform", "/r/ornek-berber"];
const whatsapp = /^https:\/\/wa\.me\/905359779382\?text=/;

test("sadece-site modu: pilot çağrıları, Çok yakında, API isteği yok, kırık link yok @sadece-site", async ({ page, visit, guard, request }, info) => {
  const apiRequests: string[] = [];
  page.on("request", (r) => {
    if (r.url().startsWith(target().apiUrl)) apiRequests.push(r.url());
  });
  const internalLinks = new Set<string>();

  for (const path of sitePages) {
    await visit(page, path, `Sadece site · ${path}`);
    // Kayda, girişe veya panele giden bağlantı kalmadı.
    await expect(page.locator('a[href^="/kayit"], a[href^="/giris"], a[href^="/panel"], a[href^="/r/"]')).toHaveCount(0);
    for (const href of await page.locator('a[href^="/"]').evaluateAll((els) => els.map((e) => e.getAttribute("href")!))) {
      internalLinks.add(href.split("#")[0] || "/");
    }
    if (path !== "/") await expect(page.locator('link[rel="canonical"]')).toHaveAttribute("href", `https://kobifabrika.com${path}`);
  }

  // Ana sayfa: ilk ekranda ve telefonda altta sabit "Pilot listesine katıl"; "Giriş yap" yok.
  await visit(page, "/", "Sadece site · ana sayfa çağrıları");
  await expect(page.getByRole("link", { name: /Ücretsiz dene|Giriş yap/ })).toHaveCount(0);
  const hero = page.getByRole("region", { name: /Kasan, veresiyen/ });
  const sticky = page.locator(".fixed").getByRole("link", { name: "Pilot listesine katıl" });
  if (isDesktop(info)) {
    await expect(hero.getByRole("link", { name: "Pilot listesine katıl" })).toHaveAttribute("href", whatsapp);
    await expect(sticky).toBeHidden();
  } else {
    await expect(sticky).toBeVisible();
    await expect(sticky).toHaveAttribute("href", whatsapp);
    await page.getByRole("button", { name: "Menü" }).click();
    const menu = page.getByRole("dialog");
    await expect(menu.getByRole("link", { name: "Pilot listesine katıl" })).toHaveAttribute("href", whatsapp);
    await expect(menu.getByRole("link", { name: "Giriş yap" })).toHaveCount(0);
    await page.keyboard.press("Escape");
  }
  const message = decodeURIComponent((await page.getByRole("link", { name: "Pilot listesine katıl" }).first().getAttribute("href"))!);
  expect(message).toContain("KobiFabrika pilot listesine katılmak istiyorum");
  await expect(page.getByRole("contentinfo").getByRole("link", { name: "Pilot listesine katıl" })).toHaveAttribute("href", whatsapp);

  // Fiyatlar: paket düğmeleri paket adıyla WhatsApp'a.
  await visit(page, "/fiyatlar", "Sadece site · fiyatlar");
  for (const name of ["Çırak", "Kalfa", "Usta"]) {
    const card = page.getByRole("listitem").filter({ has: page.getByRole("heading", { name }) });
    const cta = card.getByRole("link", { name: "Pilot listesine katıl" });
    await expect(cta).toHaveAttribute("href", whatsapp);
    expect(decodeURIComponent((await cta.getAttribute("href"))!)).toContain(`${name} paketiyle`);
  }

  // Yardım "İlk kurulum": arayüzdeki düğmenin adı.
  await visit(page, "/yardim/ilk-kurulum", "Sadece site · ilk kurulum");
  await expect(page.getByText('"Pilot listesine katıl" düğmesine dokun')).toBeVisible();
  await expect(page.getByText("Ücretsiz dene")).toHaveCount(0);

  // Uygulama yolları: aynı adreste "Çok yakında", arama motorlarına kapalı.
  for (const path of appPages) {
    await visit(page, path, `Sadece site · ${path}`);
    expect(new URL(page.url()).pathname).toBe(path);
    await expect(page.getByRole("heading", { level: 1, name: "Çok yakında" })).toBeVisible();
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute("content", /noindex/);
    await expect(page.getByRole("link", { name: "Pilot listesine katıl" })).toHaveAttribute("href", whatsapp);
    await expect(page.getByRole("link", { name: "Tanıtım sitesine dön" })).toHaveAttribute("href", "https://kobifabrika.com");
  }

  // 404'te "Panele git" yok.
  guard.allow(404, /\/olmayan-sayfa$/);
  await visit(page, "/olmayan-sayfa", "Sadece site · 404");
  await expect(page.getByRole("heading", { name: "Aradığın sayfa burada değil" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Panele git" })).toHaveCount(0);

  expect(apiRequests, "tarayıcı API'ye istek attı").toEqual([]);

  // Sitedeki bütün iç bağlantılar açılır (kırık link yok).
  for (const href of internalLinks) {
    const res = await request.get(href);
    expect(res.status(), href).toBe(200);
  }

  // SEO dosyaları: sadece site sayfaları, kök alan.
  const xml = await (await request.get("/sitemap.xml")).text();
  expect(xml).toContain("<loc>https://kobifabrika.com/fiyatlar</loc>");
  for (const path of ["/giris", "/kayit", "/panel", "/cok-yakinda", "/r/"]) expect(xml).not.toContain(path);
  const robots = await (await request.get("/robots.txt")).text();
  for (const path of ["/giris", "/kayit", "/panel", "/cok-yakinda"]) expect(robots).toContain(`Disallow: ${path}`);
  expect(robots).toContain("Sitemap: https://kobifabrika.com/sitemap.xml");
});
