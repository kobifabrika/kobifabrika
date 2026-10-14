/**
 * SMS segment hesabı ve kampanya mesajı şablonu (backend `Domain/Messaging/SmsSegments.cs` ve
 * `Domain/Marketing/MarketingMessage.cs` ile aynı algoritma ve test vektörleri).
 * GSM-7: tek parça 160, çok parçada 153 karakter; genişletilmiş karakterler (^ { } \ [ ~ ] | €) 2 sayılır.
 * Türkçe karakter (ş, ğ, ı, İ, Ş, Ğ, ç) varsa mesaj UCS-2 gider: tek parça 70, çok parçada 67.
 */

const BASIC =
  "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
const EXTENDED = "^{}\\[~]|€\f";

const basicSet = new Set(BASIC);
const extendedSet = new Set(EXTENDED);

export type SmsSize = { length: number; segments: number; unicode: boolean };

export function isGsm7(text: string): boolean {
  for (const ch of text) {
    if (!basicSet.has(ch) && !extendedSet.has(ch)) return false;
  }
  return true;
}

/** UTF-16 kod birimi sayısı (backend `string.Length` ile aynı). */
function units(text: string) {
  return text.length;
}

export function measureSms(text: string): SmsSize {
  if (text.length === 0) return { length: 0, segments: 0, unicode: false };
  if (isGsm7(text)) {
    let septets = 0;
    for (const ch of text) septets += extendedSet.has(ch) ? 2 : 1;
    return { length: septets, segments: septets <= 160 ? 1 : Math.ceil(septets / 153), unicode: false };
  }
  const n = units(text);
  return { length: n, segments: n <= 70 ? 1 : Math.ceil(n / 67), unicode: true };
}

export const FIRST_NAME_VARIABLE = "{ad}";
export const BUSINESS_VARIABLE = "{isletme}";

/** Şablondaki bilinmeyen değişkenler ({soyad} gibi). */
export function unknownVariables(body: string): string[] {
  const found = body.match(/\{[^{}]*\}/g) ?? [];
  return [...new Set(found.filter((v) => v !== FIRST_NAME_VARIABLE && v !== BUSINESS_VARIABLE))];
}

export function renderTemplate(body: string, firstName: string, businessName: string): string {
  return body.replaceAll(FIRST_NAME_VARIABLE, firstName.trim()).replaceAll(BUSINESS_VARIABLE, businessName.trim()).trim();
}

/** Gövde + boşluk + çıkış eki (sunucudaki örnek ek: compose-info → footerSample). */
export function withFooter(rendered: string, footer: string): string {
  return `${rendered} ${footer}`;
}

/**
 * Yazarken sayaç: örnek adla (varsayılan "Ayşe") çıkış eki dahil karakter ve SMS sayısı. Gerçek sayı alıcının adına göre
 * değişebilir; önizleme gerçek alıcılarla hesaplar.
 */
export function composeCounter(body: string, businessName: string, footer: string, sampleName = "Ayşe") {
  const text = withFooter(renderTemplate(body, sampleName, businessName), footer);
  return { text, ...measureSms(text) };
}
