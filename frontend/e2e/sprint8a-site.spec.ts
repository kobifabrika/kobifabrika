import { expect, isDesktop, test } from "./support/test";

/**
 * Sprint 8A tanıtım sitesi: ana sayfa (ilk ekranda "Ücretsiz dene"; telefonda altta sabit), fiyatlar (Aylık/Yıllık anahtarı ve
 * kayıt linki), SSS, Hakkımızda, İletişim, Yardım (arama ve makale), hukuki metinler; 404 sayfası; sitemap, robots, OG görseli.
 * Görsellerin alt metni axe ile (image-alt) ve aşağıda açıkça kontrol edilir.
 */
test("tanıtım sitesi: sayfalar, çağrılar, fiyat, yardım, 404 ve SEO dosyaları", async ({ page, visit, guard, request }, info) => {
  await visit(page, "/", "Site · ana sayfa");
  const hero = page.getByRole("region", { name: /Kasan, veresiyen/ });
  await expect(page.getByRole("heading", { level: 1 })).toContainText("tek uygulamada");
  const sticky = page.locator(".fixed").getByRole("link", { name: /Ücretsiz dene/ });
  if (isDesktop(info)) {
    await expect(hero.getByRole("link", { name: "Ücretsiz dene" })).toBeVisible();
    await expect(sticky).toBeHidden();
  } else {
    await expect(sticky).toBeVisible();
    await expect(sticky).toHaveAttribute("href", "/kayit");
  }
  // Bütün görsellerin anlamlı alt metni var.
  for (const img of await page.locator("img").all()) expect((await img.getAttribute("alt"))?.length ?? 0).toBeGreaterThan(5);

  await visit(page, "/fiyatlar", "Site · fiyatlar");
  const kalfa = page.getByRole("listitem").filter({ has: page.getByRole("heading", { name: "Kalfa" }) });
  await expect(kalfa.getByText("699 TL")).toBeVisible();
  await page.getByRole("radio", { name: /Yıllık/ }).click();
  await expect(kalfa.getByText("583 TL")).toBeVisible();
  await expect(kalfa.getByText("Yılda 6.990 TL (2 ay hediye)")).toBeVisible();
  await expect(kalfa.getByRole("link", { name: "14 gün ücretsiz dene" })).toHaveAttribute("href", "/kayit?paket=kalfa&donem=yillik");
  // Paket düğmeleri ilk ekranda (2026-10-21): masaüstünde 1920×1080 ve 1366×768'de üçü de, telefonda kart başına düğme fiyatın altında.
  const cards = page.getByRole("list").filter({ has: page.getByRole("heading", { name: "Kalfa" }) }).first().locator(":scope > li");
  await expect(cards).toHaveCount(3);
  if (isDesktop(info)) {
    for (const size of [{ width: 1920, height: 1080 }, { width: 1366, height: 768 }]) {
      await page.setViewportSize(size);
      await page.evaluate(() => window.scrollTo(0, 0));
      for (const card of await cards.all()) await expect(card.getByRole("link")).toBeInViewport({ ratio: 1 });
    }
    await page.setViewportSize({ width: 1280, height: 800 });
  } else {
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect(cards.first().getByRole("link")).toBeInViewport({ ratio: 1 });
  }
  for (const card of await cards.all()) {
    const price = await card.locator("p.font-heading").boundingBox();
    const cta = await card.getByRole("link").boundingBox();
    const features = await card.getByRole("listitem").first().boundingBox();
    expect(cta!.y).toBeGreaterThan(price!.y);
    expect(features!.y).toBeGreaterThan(cta!.y);
  }

  await visit(page, "/sss", "Site · SSS");
  await page.getByText("Pilot dönemi ne demek?").click();
  await expect(page.getByText(/pilot işletmeler bütün özellikleri süresiz/)).toBeVisible();
  await visit(page, "/hakkimizda", "Site · hakkımızda");
  await visit(page, "/iletisim", "Site · iletişim");
  await expect(page.getByRole("link", { name: "WhatsApp'tan yaz" })).toHaveAttribute("href", /^https:\/\/wa\.me\/905359779382/);

  await visit(page, "/yardim", "Site · yardım");
  await page.getByRole("searchbox", { name: "Yardımda ara" }).fill("whatsapp");
  await expect(page.getByRole("link", { name: /Yarının randevularını WhatsApp'tan hatırlatma/ })).toBeVisible();
  await expect(page.getByRole("link", { name: /İlk kurulum/ })).toHaveCount(0);
  for (const slug of ["ilk-kurulum", "online-randevu-linki", "whatsapp-hatirlatma", "telefonda-kullanim"]) {
    await visit(page, `/yardim/${slug}`, `Yardım · ${slug}`);
    await expect(page.locator("article img")).toHaveAttribute("alt", /.{20,}/);
  }

  // 404: Türkçe ve yönlendirici.
  guard.allow(404, /\/olmayan-sayfa$/);
  await visit(page, "/olmayan-sayfa", "Site · 404");
  await expect(page.getByRole("heading", { name: "Aradığın sayfa burada değil" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Ana sayfaya dön" })).toBeVisible();

  // SEO dosyaları.
  const sitemap = await request.get("/sitemap.xml");
  expect(sitemap.ok()).toBe(true);
  const xml = await sitemap.text();
  expect(xml).toContain("/fiyatlar");
  expect(xml).toContain("/yardim/ilk-kurulum");
  expect(xml).not.toContain("/panel");
  const robots = await (await request.get("/robots.txt")).text();
  expect(robots).toContain("Disallow: /panel");
  expect(robots).toContain("Disallow: /r/");
  const og = await request.get("/opengraph-image");
  expect(og.ok()).toBe(true);
  expect(og.headers()["content-type"]).toContain("image/png");
  for (const icon of ["/favicon.ico", "/icon.png", "/apple-icon.png", "/manifest.webmanifest"]) expect((await request.get(icon)).ok()).toBe(true);
});
