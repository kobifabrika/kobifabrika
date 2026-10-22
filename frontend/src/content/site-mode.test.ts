import { NextRequest } from "next/server";
import { afterEach, describe, expect, it, vi } from "vitest";

/** Mod derlemeye gömülü olduğu için modüller her durumda ortam değişkeni ayarlandıktan sonra yeniden yüklenir. */
async function load(siteOnly: boolean) {
  vi.resetModules();
  if (siteOnly) vi.stubEnv("NEXT_PUBLIC_SITE_ONLY", "1");
  const [site, plans, help, robots, proxy] = await Promise.all([
    import("./site"),
    import("./plans"),
    import("./help"),
    import("@/app/robots"),
    import("@/proxy"),
  ]);
  return { site, plans, help, robots: robots.default(), proxy: proxy.proxy };
}

const request = (path: string) => new NextRequest(new URL(path, "http://localhost:3000"));

afterEach(() => {
  vi.unstubAllEnvs();
});

describe("sadece-site modu kapalı (varsayılan)", () => {
  it("çağrılar kayda, alt bilgide giriş var", async () => {
    const { site, plans } = await load(false);
    expect(site.primaryCta).toEqual({ href: "/kayit", label: "Ücretsiz dene" });
    expect(site.showLogin).toBe(true);
    expect(site.footerColumns.flatMap((c) => c.links.map((l) => l.href))).toContain("/giris");
    const kalfa = plans.plans.find((p) => p.id === "kalfa")!;
    expect(plans.signupHref(kalfa, "yillik")).toBe("/kayit?paket=kalfa&donem=yillik");
    expect(plans.planCta(kalfa)).toBe(kalfa.cta);
  });

  it("İlk kurulum makalesi ana çağrının adını kullanır", async () => {
    const { site, help } = await load(false);
    const [first] = help.helpArticles.find((a) => a.slug === "ilk-kurulum")!.steps;
    expect(first.title).toBe("Üye ol");
    expect(first.text).toContain(`"${site.primaryCta.label}" düğmesine dokun`);
    expect(first.text).toContain("Ücretsiz dene");
  });

  it("proxy: oturumsuz panel girişe yönlenir, giriş ve randevu linki olduğu gibi geçer", async () => {
    const { proxy } = await load(false);
    const panel = proxy(request("/panel/musteriler"));
    expect(panel.headers.get("location")).toBe("http://localhost:3000/giris?donus=%2Fpanel%2Fmusteriler");
    for (const path of ["/giris", "/kayit", "/r/ornek-berber"]) {
      const res = proxy(request(path));
      expect(res.headers.get("location")).toBeNull();
      expect(res.headers.get("x-middleware-rewrite")).toBeNull();
    }
  });

  it("robots giriş ve kaydı kapatmaz", async () => {
    const { robots } = await load(false);
    const rules = Array.isArray(robots.rules) ? robots.rules[0] : robots.rules;
    expect(rules.disallow).not.toContain("/giris");
  });
});

describe("sadece-site modu açık (NEXT_PUBLIC_SITE_ONLY=1)", () => {
  it("İlk kurulum makalesi pilot düğmesini anlatır, kayıt formundan söz etmez", async () => {
    const { help } = await load(true);
    const [first] = help.helpArticles.find((a) => a.slug === "ilk-kurulum")!.steps;
    expect(first.title).toBe("Pilot listesine katıl");
    expect(first.text).toContain('"Pilot listesine katıl" düğmesine dokun');
    expect(first.text).not.toContain("Ücretsiz dene");
    expect(first.text).not.toContain("şifreni");
  });

  it("bütün çağrılar WhatsApp'tan pilot listesine; giriş bağlantısı yok", async () => {
    const { site, plans } = await load(true);
    expect(site.primaryCta.label).toBe("Pilot listesine katıl");
    expect(site.primaryCta.href).toMatch(/^https:\/\/wa\.me\/90\d{10}\?text=/);
    expect(decodeURIComponent(site.primaryCta.href)).toContain("pilot listesine katılmak istiyorum");
    expect(site.stickyCtaLabel).toBe("Pilot listesine katıl");
    expect(site.showLogin).toBe(false);
    const hrefs = site.footerColumns.flatMap((c) => c.links.map((l) => l.href));
    expect(hrefs.filter((h) => h.startsWith("/kayit") || h.startsWith("/giris"))).toEqual([]);
    for (const plan of plans.plans) {
      expect(plans.planCta(plan)).toBe("Pilot listesine katıl");
      const href = plans.signupHref(plan, "aylik");
      expect(href).toMatch(/^https:\/\/wa\.me\//);
      expect(decodeURIComponent(href)).toContain(`${plan.name} paketiyle`);
    }
  });

  it("proxy: giriş, kayıt, panel ve link sayfaları Çok yakında'yı gösterir (yönlendirmeden)", async () => {
    const { proxy } = await load(true);
    for (const path of ["/giris", "/kayit", "/panel", "/panel/musteriler", "/platform", "/r/ornek-berber", "/teklif/abc"]) {
      const res = proxy(request(path));
      expect(res.headers.get("location")).toBeNull();
      expect(res.headers.get("x-middleware-rewrite")).toBe("http://localhost:3000/cok-yakinda");
    }
  });

  it("robots giriş, kayıt ve Çok yakında'yı da kapatır; site haritası adresi kök alan", async () => {
    const { robots } = await load(true);
    const rules = Array.isArray(robots.rules) ? robots.rules[0] : robots.rules;
    expect(rules.disallow).toEqual(expect.arrayContaining(["/giris", "/kayit", "/cok-yakinda", "/panel"]));
    expect(robots.sitemap).toBe("https://kobifabrika.com/sitemap.xml");
  });
});
