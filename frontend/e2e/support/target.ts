import { apiUrl, smsApiUrl, smsWebUrl, webUrl } from "../scripts/env.mjs";

/**
 * Testin konuştuğu E2E çifti. Varsayılan çift SMS kapalı (pilotun gerçek hali: API 5180, web 3100); `@sms` etiketli senaryolar
 * "-sms" projelerinde SMS açık ikinci çiftte (API 5181, web 3101, KobiFabrika_E2E_Sms) koşar. Değeri her worker'ın başında
 * support/test.ts'teki otomatik worker düzeneği ayarlar (bir worker tek projenin testlerini koşar).
 */
let sms = false;

export function setSmsTarget(value: boolean) {
  sms = value;
}

export function target(): { apiUrl: string; webUrl: string; sms: boolean } {
  return sms ? { apiUrl: smsApiUrl, webUrl: smsWebUrl, sms } : { apiUrl, webUrl, sms };
}

/** Proje adından: "telefon-chromium-sms" → SMS açık çift. */
export const isSmsProject = (projectName: string) => projectName.endsWith("-sms");
