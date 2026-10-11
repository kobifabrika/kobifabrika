import { defineConfig, devices } from "@playwright/test";

import { apiUrl, webUrl } from "./e2e/scripts/env.mjs";

/**
 * Uçtan uca testler (frontend/e2e). Ayrı API (5180) ve frontend (3100), ayrı veritabanı (KobiFabrika_E2E, her çalıştırmada
 * sıfırdan). Geliştirme ortamına dokunmaz. Tek komut: repo kökünde .\test.ps1 (veya .\test.ps1 -E2E).
 *
 * Ortam değişkenleri: E2E_HEADED=1 tarayıcı görünür ve yavaş (izlemek için), E2E_SKIP_API_BUILD / E2E_SKIP_WEB_BUILD=1
 * derlemeyi atlar (test.ps1 önceden derler).
 */
const headed = process.env.E2E_HEADED === "1";
const ci = !!process.env.CI;

/** Telefon: 375 px genişlik, dokunmatik. 2x piksel (3x'te uzun sayfaların tam ekran görüntüsü WebKit sınırını aşıyor). */
const phone = {
  ...devices["iPhone 13"],
  viewport: { width: 375, height: 812 },
  screen: { width: 375, height: 812 },
  deviceScaleFactor: 2,
};

export default defineConfig({
  testDir: "./e2e",
  outputDir: "./test-results/artifacts",
  timeout: 120_000,
  expect: { timeout: 10_000 },
  fullyParallel: true,
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  workers: headed ? 1 : ci ? 2 : 4,
  reporter: [
    ["list"],
    ["html", { open: "never", outputFolder: "playwright-report" }],
    ["json", { outputFile: "test-results/results.json" }],
  ],
  use: {
    baseURL: webUrl,
    locale: "tr-TR",
    timezoneId: "Europe/Istanbul",
    headless: !headed,
    launchOptions: { slowMo: headed ? 350 : 0 },
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    trace: "retain-on-failure",
    video: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "masaustu", use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 800 } } },
    { name: "telefon-chromium", use: { ...phone, browserName: "chromium" } },
    { name: "telefon-webkit", use: { ...phone, browserName: "webkit" } },
  ],
  webServer: [
    {
      name: "API",
      command: "node e2e/scripts/start-api.mjs",
      url: `${apiUrl}/health`,
      timeout: 300_000,
      reuseExistingServer: false,
      stdout: "pipe",
      stderr: "pipe",
    },
    {
      name: "Web",
      command: "node e2e/scripts/start-web.mjs",
      url: webUrl,
      timeout: 300_000,
      reuseExistingServer: false,
      stdout: "pipe",
      stderr: "pipe",
    },
  ],
});
