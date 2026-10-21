import type { MetadataRoute } from "next";

import { siteUrl } from "@/content/seo";
import { isSiteOnly } from "@/content/site-mode";

/**
 * Panel, süper admin, online randevu ve tek kullanımlık link sayfaları arama motorlarına kapalı. Sadece-site modunda giriş ve
 * kayıt da ("Çok yakında" gösterirler).
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: [
        "/panel",
        "/platform",
        "/kurulum",
        "/r/",
        "/a/",
        "/c/",
        "/ekstre/",
        "/odeme/",
        "/teklif/",
        "/davet/",
        "/e-posta-dogrula",
        "/sifre-sifirla",
        ...(isSiteOnly() ? ["/giris", "/kayit", "/sifremi-unuttum", "/cok-yakinda"] : []),
      ],
    },
    sitemap: `${siteUrl}/sitemap.xml`,
    host: siteUrl,
  };
}
