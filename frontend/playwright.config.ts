import { defineConfig, devices } from "@playwright/test";

import { apiUrl, smsApiUrl, smsPairEnabled, smsWebUrl, webUrl } from "./e2e/scripts/env.mjs";

/**
 * Uçtan uca testler (frontend/e2e). Ayrı API (5180) ve frontend (3100), ayrı veritabanı (KobiFabrika_E2E, her çalıştırmada
 * sıfırdan). Geliştirme ortamına dokunmaz. Tek komut: repo kökünde .\test.ps1 (veya .\test.ps1 -E2E).
 *
 * İki çift (karar 2026-10-17): varsayılan çift SMS kapalı koşar (pilotun gerçek hali). Başlığında `@sms` olan senaryolar SMS açık
 * ikinci çiftte (API 5181, web 3101, KobiFabrika_E2E_Sms) "-sms" projelerinde koşar; aynı üç profil. E2E_SMS=0: SMS çifti açılmaz.
 *
 * Ortam değişkenleri: E2E_HEADED=1 tarayıcı görünür ve yavaş (izlemek için), E2E_SKIP_API_BUILD / E2E_SKIP_WEB_BUILD=1
 * derlemeyi atlar (test.ps1 önceden derler).
 */
const headed = process.env.E2E_HEADED === "1";
const ci = !!process.env.CI;
/** E2E_REUSE=1: açık E2E sunucularını kullan (test yazarken hızlı tekrar; veritabanı sıfırlanmaz). */
const reuse = process.env.E2E_REUSE === "1";

/** Telefon: 375 px genişlik, dokunmatik. 2x piksel (3x'te uzun sayfaların tam ekran görüntüsü WebKit sınırını aşıyor). */
const phone = {
  ...devices["iPhone 13"],
  viewport: { width: 375, height: 812 },
  screen: { width: 375, height: 812 },
  deviceScaleFactor: 2,
};

const profiles = [
  { name: "masaustu", use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 800 } } },
  { name: "telefon-chromium", use: { ...phone, browserName: "chromium" as const } },
  { name: "telefon-webkit", use: { ...phone, browserName: "webkit" as const } },
];

/** İki çift aynı anda başlar; derlemeler kilitle sıraya girdiği için süre sınırı geniş (ikinci web derlemesi birincinin ardından). */
const server = (name: string, command: string, url: string) => ({
  name,
  command,
  url,
  timeout: 600_000,
  reuseExistingServer: reuse,
  stdout: "pipe" as const,
  stderr: "pipe" as const,
});

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
    ...profiles.map((p) => ({ name: p.name, grepInvert: /@sms/, use: p.use })),
    ...(smsPairEnabled ? profiles.map((p) => ({ name: `${p.name}-sms`, grep: /@sms/, use: { ...p.use, baseURL: smsWebUrl } })) : []),
  ],
  webServer: [
    server("API", "node e2e/scripts/start-api.mjs", `${apiUrl}/health`),
    server("Web", "node e2e/scripts/start-web.mjs", webUrl),
    ...(smsPairEnabled
      ? [
          server("API (SMS)", "node e2e/scripts/start-api.mjs --sms", `${smsApiUrl}/health`),
          server("Web (SMS)", "node e2e/scripts/start-web.mjs --sms", smsWebUrl),
        ]
      : []),
  ],
});
