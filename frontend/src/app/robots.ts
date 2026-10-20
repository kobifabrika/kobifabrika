import type { MetadataRoute } from "next";

import { siteUrl } from "@/content/seo";

/** Panel, süper admin, online randevu ve tek kullanımlık link sayfaları arama motorlarına kapalı. */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/panel", "/platform", "/kurulum", "/r/", "/a/", "/c/", "/ekstre/", "/odeme/", "/teklif/", "/davet/", "/e-posta-dogrula", "/sifre-sifirla"],
    },
    sitemap: `${siteUrl}/sitemap.xml`,
    host: siteUrl,
  };
}
