// E2E API'si (Playwright webServer): KobiFabrika_E2E veritabanını silip migration'larla sıfırdan kurar, API'yi Release
// derlemesiyle ayrı portta başlatır. Ortam "E2E": user-secrets okunmaz, bağlantı dizesi sadece buradan gelir.
// E2E_SKIP_API_BUILD=1: derlemeyi atla (test.ps1 backend testlerinde Release derlemesini zaten yaptı).
import { spawn, spawnSync } from "node:child_process";
import { mkdirSync, rmSync } from "node:fs";
import path from "node:path";

import { apiPort, connectionString, repoRoot, runDir, webUrl } from "./env.mjs";

const apiProject = path.join(repoRoot, "backend", "src", "KobiFabrika.Api");
const infraProject = path.join(repoRoot, "backend", "src", "KobiFabrika.Infrastructure");
const binDir = path.join(apiProject, "bin", "Release", "net10.0");
const apiRunDir = path.join(runDir, "api");

const env = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "E2E",
  DOTNET_ENVIRONMENT: "E2E",
  ConnectionStrings__Default: connectionString(),
  App__FrontendUrl: webUrl,
  Cors__AllowedOrigins__0: webUrl,
  // http://localhost üzerinden çerez (canlıda kapalı, sadece HTTPS).
  Auth__AllowHttpCookie: "true",
  // Testler aynı IP'den çok sayıda kayıt/giriş yapar; canlıdaki sınırlar değişmez.
  RateLimiting__AuthPermitLimit: "100000",
  RateLimiting__PublicPermitLimit: "100000",
  RateLimiting__BookingReadPermitLimit: "100000",
  RateLimiting__BookingSmsPermitLimit: "100000",
  // Sahte SMS sağlayıcısı her SMS'i sms+905…@sms.test adresine Mailpit'e de yollar; E2E doğrulama kodunu oradan okur.
  Sms__MirrorToEmail: "true",
  // Kampanya SMS'leri her saatte gider (CI gece de koşar; 09:00-21:00 kuralı birim/entegrasyon testlerinde).
  Marketing__SendWindowStart: "00:00",
  Marketing__SendWindowEnd: "24:00",
  // Çıkış linki E2E frontend'ine işaret etsin (canlıda kobifabrika.com).
  PublicLinks__ShortBaseUrl: webUrl,
  // Kredi yükleme ekranı için platform yöneticisi (E2E'de bu adresle kayıt olan kullanıcı).
  Platform__AdminEmails__0: "e2e-platform@example.test",
  Storage__LocalPath: path.join(apiRunDir, "files"),
  DataProtection__KeysPath: path.join(apiRunDir, "keys"),
  Serilog__MinimumLevel__Default: "Warning",
  DOTNET_NOLOGO: "1",
  DOTNET_CLI_TELEMETRY_OPTOUT: "1",
};

function run(args, what) {
  console.log(`[e2e-api] ${what}`);
  const result = spawnSync("dotnet", args, { cwd: repoRoot, env, stdio: "inherit" });
  if (result.status !== 0) {
    console.error(`[e2e-api] HATA: ${what} başarısız (çıkış kodu ${result.status}).`);
    process.exit(1);
  }
}

const efOptions = [
  "--project",
  infraProject,
  "--startup-project",
  apiProject,
  "--configuration",
  "Release",
  "--no-build",
];
if (process.env.E2E_SKIP_API_BUILD !== "1") {
  run(["build", apiProject, "-c", "Release", "-v", "q", "--nologo"], "API derleniyor (Release)");
}
run(["ef", "database", "drop", "--force", ...efOptions], "E2E veritabanı siliniyor");
run(["ef", "database", "update", ...efOptions], "E2E veritabanı migration'larla kuruluyor");

rmSync(apiRunDir, { recursive: true, force: true });
mkdirSync(apiRunDir, { recursive: true });

console.log(`[e2e-api] API başlıyor: http://localhost:${apiPort}`);
const api = spawn(
  "dotnet",
  [path.join(binDir, "KobiFabrika.Api.dll"), "--urls", `http://localhost:${apiPort}`, "--contentRoot", binDir],
  { cwd: apiRunDir, env, stdio: "inherit" },
);
const stop = () => api.kill();
process.on("SIGINT", stop);
process.on("SIGTERM", stop);
api.on("exit", (code) => process.exit(code ?? 0));
