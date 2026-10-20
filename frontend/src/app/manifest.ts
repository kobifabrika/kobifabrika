import type { MetadataRoute } from "next";

/** Ana ekrana ekleme (telefonda "Uygulama gibi" açılır). İkonlar `npm run marka` ile public/marka/isaret.svg'den üretilir. */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "KobiFabrika",
    short_name: "KobiFabrika",
    description: "KOBİ'nin dijital fabrikası: kasa, veresiye, randevu ve personel tek uygulamada.",
    lang: "tr",
    start_url: "/panel",
    display: "standalone",
    background_color: "#ffffff",
    theme_color: "#000000",
    icons: [
      { src: "/marka/icon-192.png", sizes: "192x192", type: "image/png" },
      { src: "/icon.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/marka/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
