import { defineConfig, devices } from "@playwright/test";

import { apiUrl, webUrl } from "./e2e/scripts/env.mjs";

/**
 * Demo ekran görüntüleri (`npm run demo:shots`): "Işıltı Kuaför" demo işletmesini E2E ortamında (KobiFabrika_E2E, SMS kapalı; geliştirme
 * veritabanına dokunmaz) normal API uçlarıyla kurar ve tanıtım sitesi (public/site) ile yardım makaleleri (public/yardim) için
 * WebP ekran görüntüleri alır. CI'a girmez (testMatch *.demo.ts; ana yapılandırma bunları görmez).
 */
export default defineConfig({
  testDir: "./e2e/demo",
  testMatch: "**/*.demo.ts",
  outputDir: "./test-results/demo",
  timeout: 600_000,
  workers: 1,
  reporter: [["list"]],
  use: { baseURL: webUrl, locale: "tr-TR", timezoneId: "Europe/Istanbul", headless: true },
  projects: [{ name: "demo", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    { name: "API", command: "node e2e/scripts/start-api.mjs", url: `${apiUrl}/health`, timeout: 600_000, stdout: "pipe", stderr: "pipe" },
    { name: "Web", command: "node e2e/scripts/start-web.mjs", url: webUrl, timeout: 600_000, stdout: "pipe", stderr: "pipe" },
  ],
});
