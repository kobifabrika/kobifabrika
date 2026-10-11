import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // E2E testleri ayrı klasöre derler (NEXT_DIST_DIR=.next-e2e); açık olan dev/-Hizli sunucusunun .next'ine dokunmaz.
  distDir: process.env.NEXT_DIST_DIR || ".next",
  cacheComponents: true,
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
