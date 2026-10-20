import Script from "next/script";

/**
 * Cloudflare Web Analytics (çerezsiz, kişiyi izlemez; çerez bannerı gerekmez — gizlilik ve çerez politikasında yazar). Token
 * NEXT_PUBLIC_CF_ANALYTICS_TOKEN'dan (.env.example'da boş); boşsa betik eklenmez (geliştirme, E2E).
 */
export function SiteAnalytics() {
  const token = process.env.NEXT_PUBLIC_CF_ANALYTICS_TOKEN?.trim();
  if (!token) return null;
  return (
    <Script
      defer
      strategy="afterInteractive"
      src="https://static.cloudflareinsights.com/beacon.min.js"
      data-cf-beacon={JSON.stringify({ token })}
    />
  );
}
