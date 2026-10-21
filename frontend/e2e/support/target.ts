import { apiUrl, siteApiUrl, siteWebUrl, smsApiUrl, smsWebUrl, webUrl } from "../scripts/env.mjs";

/**
 * Testin konuştuğu E2E çifti. Varsayılan çift SMS kapalı (pilotun gerçek hali: API 5180, web 3100); `@sms` etiketli senaryolar
 * "-sms" projelerinde SMS açık ikinci çiftte (API 5181, web 3101, KobiFabrika_E2E_Sms) koşar; `@sadece-site` senaryoları "-site"
 * projelerinde API'siz sadece-site derlemesinde (web 3102; API adresi boş port). Değeri her worker'ın başında support/test.ts'teki
 * otomatik worker düzeneği ayarlar (bir worker tek projenin testlerini koşar).
 */
type Kind = "default" | "sms" | "site";
let kind: Kind = "default";

export function setTargetFromProject(projectName: string) {
  kind = projectName.endsWith("-sms") ? "sms" : projectName.endsWith("-site") ? "site" : "default";
}

export function target(): { apiUrl: string; webUrl: string; sms: boolean } {
  if (kind === "sms") return { apiUrl: smsApiUrl, webUrl: smsWebUrl, sms: true };
  if (kind === "site") return { apiUrl: siteApiUrl, webUrl: siteWebUrl, sms: false };
  return { apiUrl, webUrl, sms: false };
}
