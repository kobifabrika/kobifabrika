// E2E frontend'i (Playwright webServer): production build (API adresi E2E API'si) + next start, ayrı portta.
// Geliştirme sunucusunun .next klasörüne dokunmaz. Varsayılan: .next-e2e, 3100 → API 5180 (SMS kapalı). --sms: .next-e2e-sms,
// 3101 → API 5181 (SMS açık). NEXT_PUBLIC_API_URL derlemeye gömüldüğü için iki çiftin derlemesi ayrıdır; aynı anda başlarlarsa
// derlemeler sıraya girer. E2E_SKIP_WEB_BUILD=1: derleme önceden yapıldı (test.ps1, CI).
import { spawn, spawnSync } from "node:child_process";
import path from "node:path";

import { withLock } from "./lock.mjs";
import { apiUrl, frontendDir, runDir, smsApiUrl, smsWebPort, webPort } from "./env.mjs";

const sms = process.argv.includes("--sms");
// E2E_WEB_DEV=1: derleme yok, `next dev` (React'in geliştirme uyarıları konsola düşer; rol turu bunları toplar). CI'da kullanılmaz.
const dev = process.env.E2E_WEB_DEV === "1";
const tag = sms ? "e2e-web-sms" : "e2e-web";
const distDir = (sms ? ".next-e2e-sms" : ".next-e2e") + (dev ? "-dev" : "");
const port = sms ? smsWebPort : webPort;
const nextBin = path.join(frontendDir, "node_modules", "next", "dist", "bin", "next");
const env = {
  ...process.env,
  NEXT_DIST_DIR: distDir,
  NEXT_PUBLIC_API_URL: sms ? smsApiUrl : apiUrl,
  NEXT_TELEMETRY_DISABLED: "1",
};

if (!dev && process.env.E2E_SKIP_WEB_BUILD !== "1") {
  await withLock(path.join(runDir, "web-build.lock"), () => {
    console.log(`[${tag}] Frontend derleniyor (production, ${distDir})`);
    const build = spawnSync(process.execPath, [nextBin, "build"], { cwd: frontendDir, env, stdio: "inherit" });
    if (build.status !== 0) process.exit(build.status ?? 1);
  });
}

console.log(`[${tag}] Frontend başlıyor${dev ? " (geliştirme modu)" : ""}: http://localhost:${port}`);
const web = spawn(process.execPath, [nextBin, dev ? "dev" : "start", "-p", String(port)], {
  cwd: frontendDir,
  env,
  stdio: "inherit",
});
const stop = () => web.kill();
process.on("SIGINT", stop);
process.on("SIGTERM", stop);
web.on("exit", (code) => process.exit(code ?? 0));
