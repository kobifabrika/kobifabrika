import { defineConfig } from "vitest/config";

export default defineConfig({
  resolve: { tsconfigPaths: true },
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    // findBy/waitFor sınırı 5 sn (vitest.setup.ts); test sınırı da 5 sn olunca yük altında (Actions) test, bekleme bitmeden
    // düşüyordu (import-wizard, 2026-10-21). Test sınırı beklemenin üç katı.
    testTimeout: 15_000,
  },
});
