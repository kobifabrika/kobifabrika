// E2E ortamının adresleri ve veritabanı bağlantısı. Playwright yapılandırması ve başlatma betikleri ortak kullanır.
// Geliştirme ortamından (API 5080, frontend 3000, KobiFabrika veritabanı) tamamen ayrıdır; baslat.ps1 açıkken de çalışır.
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

/** frontend klasörü (playwright.config.ts'nin yeri): çalışma klasöründen yukarı doğru aranır. Playwright bu dosyayı
 * CommonJS'e çevirdiği için import.meta kullanılamıyor. */
function findFrontendDir() {
  for (let dir = process.cwd(); ; dir = path.dirname(dir)) {
    if (existsSync(path.join(dir, "playwright.config.ts"))) return dir;
    if (existsSync(path.join(dir, "frontend", "playwright.config.ts"))) return path.join(dir, "frontend");
    if (path.dirname(dir) === dir)
      throw new Error("frontend/playwright.config.ts bulunamadı; komutu repo içinden çalıştırın.");
  }
}

export const frontendDir = findFrontendDir();
export const repoRoot = path.dirname(frontendDir);
/** API'nin çalışma klasörü (log, logo, anahtar); her çalıştırmada silinir, git'e girmez. */
export const runDir = path.join(frontendDir, "e2e", ".run");

export const apiPort = Number(process.env.E2E_API_PORT || 5180);
export const webPort = Number(process.env.E2E_WEB_PORT || 3100);
export const apiUrl = `http://localhost:${apiPort}`;
export const webUrl = `http://localhost:${webPort}`;

/** SMS açık ikinci çift (`@sms` senaryoları; karar 2026-10-17, soru 8). Varsayılan çift SMS kapalı koşar (pilotun gerçek hali). */
export const smsApiPort = Number(process.env.E2E_SMS_API_PORT || 5181);
export const smsWebPort = Number(process.env.E2E_SMS_WEB_PORT || 3101);
export const smsApiUrl = `http://localhost:${smsApiPort}`;
export const smsWebUrl = `http://localhost:${smsWebPort}`;
/**
 * Sadece-site modu (NEXT_PUBLIC_SITE_ONLY=1, `@sadece-site` senaryoları; karar 2026-10-21): API'siz tek web sunucusu. API adresi
 * bilerek boş bir port: tarayıcı oraya istek atarsa bağlantı hatası olarak yakalanır.
 */
export const siteWebPort = Number(process.env.E2E_SITE_WEB_PORT || 3102);
export const siteWebUrl = `http://localhost:${siteWebPort}`;
export const siteApiUrl = `http://localhost:${Number(process.env.E2E_SITE_API_PORT || 5182)}`;
/** E2E_SITE=0: sadece-site sunucusunu açma. */
export const sitePairEnabled = process.env.E2E_SITE !== "0";
/**
 * Koşu kimliği: Playwright yapılandırması (koşucu) ilk kez okurken üretilir; sunuculara ve worker'lara ortam değişkeniyle aynen
 * geçer. Sabit adresli hesaplar (süper admin) her koşuda ve çiftte ayrı olsun: ortak Mailpit'te başka çiftin ya da önceki
 * koşunun doğrulama e-postası okunmasın.
 */
export const runId = (process.env.E2E_RUN_ID ??= Date.now().toString(36));

/** Süper admin adresi (API: Platform:AdminEmails). */
export const platformEmail = (sms) => `e2e-platform-${sms ? "sms-" : ""}${runId}@example.test`;

/** E2E_SMS=0: SMS çiftini açma (yerelde sadece SMS kapalı senaryolar koşarken iki sunucu ve bir derleme kazanılır). */
export const smsPairEnabled = process.env.E2E_SMS !== "0";
export const mailpitUrl = process.env.E2E_MAILPIT_URL || "http://localhost:8025";

/** Sadece adı _E2E (veya _E2E_Sms) ile biten veritabanına izin verilir: geliştirme veritabanı asla silinmez. */
export const databaseName = "KobiFabrika_E2E";
export const smsDatabaseName = "KobiFabrika_E2E_Sms";

function readDotEnv(file) {
  if (!existsSync(file)) return {};
  const values = {};
  for (const line of readFileSync(file, "utf8").split(/\r?\n/)) {
    const match = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/i);
    if (match) values[match[1]] = match[2].replace(/^(['"])(.*)\1$/, "$2");
  }
  return values;
}

/** E2E_CONNECTION_STRING (CI) ya da repo kökündeki .env'deki SA şifresiyle yerel SQL Server. sms: aynı sunucuda ikinci veritabanı. */
export function connectionString(sms = false) {
  let value = process.env.E2E_CONNECTION_STRING;
  if (!value) {
    const dotenv = readDotEnv(path.join(repoRoot, ".env"));
    const password = process.env.MSSQL_SA_PASSWORD || dotenv.MSSQL_SA_PASSWORD;
    const port = process.env.MSSQL_PORT || dotenv.MSSQL_PORT || "1433";
    if (!password) throw new Error(".env içinde MSSQL_SA_PASSWORD yok (README → İlk çalıştırma).");
    value = `Server=localhost,${port};Database=${databaseName};User Id=sa;Password=${password};TrustServerCertificate=True`;
  }
  if (sms) value = value.replace(/((?:Database|Initial Catalog)\s*=\s*)([^;]+)/i, `$1${smsDatabaseName}`);
  const db = value.match(/(?:Database|Initial Catalog)\s*=\s*([^;]+)/i)?.[1]?.trim();
  if (!db || !/_E2E(_Sms)?$/i.test(db)) throw new Error(`E2E veritabanının adı _E2E ile bitmeli (bulunan: ${db ?? "yok"}).`);
  return value;
}
