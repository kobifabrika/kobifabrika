import { defineConfig } from "vitest/config";

export default defineConfig({
  resolve: { tsconfigPaths: true },
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    // findBy/waitFor sınırı 10 sn (vitest.setup.ts); eskiden test sınırıyla (5 sn) eşitti, yük altında (Actions) test, bekleme bitmeden
    // yük altında (Actions) test bekleme bitmeden düşüyordu (import-wizard, 2026-10-21).
    testTimeout: 15_000,
  },
});
