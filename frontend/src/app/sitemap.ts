import type { MetadataRoute } from "next";

import { helpArticles } from "@/content/help";
import { legalDocs } from "@/content/legal";
import { siteUrl } from "@/content/seo";

/** Site haritası: tanıtım sayfaları, yardım makaleleri ve hukuki metinler (panel, /platform, /r ve token sayfaları yok). */
export default function sitemap(): MetadataRoute.Sitemap {
  const pages = ["", "/fiyatlar", "/sss", "/hakkimizda", "/iletisim", "/yardim"];
  return [
    ...pages.map((p) => ({ url: `${siteUrl}${p}`, changeFrequency: "weekly" as const, priority: p === "" ? 1 : 0.8 })),
    ...helpArticles.map((a) => ({ url: `${siteUrl}/yardim/${a.slug}`, changeFrequency: "monthly" as const, priority: 0.6 })),
    ...Object.values(legalDocs).map((d) => ({ url: `${siteUrl}/${d.slug}`, changeFrequency: "yearly" as const, priority: 0.3 })),
  ];
}
