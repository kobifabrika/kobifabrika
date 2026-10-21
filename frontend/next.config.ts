import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // E2E testleri ayrı klasöre derler (NEXT_DIST_DIR=.next-e2e); açık olan dev/-Hizli sunucusunun .next'ine dokunmaz.
  distDir: process.env.NEXT_DIST_DIR || ".next",
  cacheComponents: true,
  // Site fotoğrafları ve ürün ekranları kalite 85 (2026-10-21; varsayılan 75 fotoğrafı ve ekrandaki yazıyı yumuşatıyordu).
  images: { qualities: [75, 85] },
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
