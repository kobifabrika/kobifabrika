// E2E frontend'i (Playwright webServer): production build (.next-e2e, API adresi E2E API'si) + next start, ayrı portta.
// Geliştirme sunucusunun .next klasörüne dokunmaz. E2E_SKIP_WEB_BUILD=1: test.ps1 build adımında zaten derledi.
import { spawn, spawnSync } from "node:child_process";
import path from "node:path";

import { apiUrl, frontendDir, webPort } from "./env.mjs";

const nextBin = path.join(frontendDir, "node_modules", "next", "dist", "bin", "next");
const env = { ...process.env, NEXT_DIST_DIR: ".next-e2e", NEXT_PUBLIC_API_URL: apiUrl, NEXT_TELEMETRY_DISABLED: "1" };

if (process.env.E2E_SKIP_WEB_BUILD !== "1") {
  console.log("[e2e-web] Frontend derleniyor (production, .next-e2e)");
  const build = spawnSync(process.execPath, [nextBin, "build"], { cwd: frontendDir, env, stdio: "inherit" });
  if (build.status !== 0) process.exit(build.status ?? 1);
}

console.log(`[e2e-web] Frontend başlıyor: http://localhost:${webPort}`);
const web = spawn(process.execPath, [nextBin, "start", "-p", String(webPort)], {
  cwd: frontendDir,
  env,
  stdio: "inherit",
});
const stop = () => web.kill();
process.on("SIGINT", stop);
process.on("SIGTERM", stop);
web.on("exit", (code) => process.exit(code ?? 0));
