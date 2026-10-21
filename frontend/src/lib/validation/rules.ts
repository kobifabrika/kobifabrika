import { parseNumberText } from "@/lib/number-input";
import { z } from "zod";

/**
 * Form kuralları. Backend'deki FluentValidation kurallarıyla ve Domain'deki PhoneNumber / SlugGenerator ile
 * aynıdır (backend/src/KobiFabrika.Api/Common/Validation.cs, KobiFabrika.Domain/Common/*). Biri değişirse diğeri de.
 */

export const passwordSchema = z
  .string()
  .min(8, "Şifre en az 8 karakter olmalı.")
  .max(128, "Şifre en fazla 128 karakter olabilir.")
  .regex(/\p{L}/u, "Şifre en az bir harf içermeli.")
  .regex(/[0-9]/, "Şifre en az bir rakam içermeli.");

/** Şifre kutusunun altındaki canlı ipuçları. */
export const passwordHints = [
  { label: "En az 8 karakter", test: (v: string) => v.length >= 8 },
  { label: "En az bir harf", test: (v: string) => /\p{L}/u.test(v) },
  { label: "En az bir rakam", test: (v: string) => /[0-9]/.test(v) },
] as const;

export const emailSchema = z
  .string()
  .trim()
  .min(1, "E-posta adresi girin.")
  .max(256, "E-posta adresi çok uzun.")
  .email("Geçerli bir e-posta adresi girin.");

export const fullNameSchema = z
  .string()
  .trim()
  .min(2, "Ad soyad girin.")
  .max(200, "Ad soyad en fazla 200 karakter olabilir.");

export const PHONE_MESSAGE = "Geçerli bir telefon numarası girin (ör. 0532 123 45 67).";

/**
 * Türkiye telefonunu E.164'e (+90XXXXXXXXXX) çevirir; geçersizse null.
 * Ulusal numara 10 hane, 2/3/4 (sabit), 5 (cep) veya 8 (850 vb.) ile başlar.
 */
export function normalizePhone(input: string | null | undefined): string | null {
  if (!input?.trim()) return null;
  const trimmed = input.trim();
  if (!/^[0-9 ()+.-]+$/.test(trimmed) || trimmed.indexOf("+") > 0) return null;

  const digits = trimmed.replace(/\D/g, "");
  const plus = trimmed.startsWith("+");
  let national: string | null = null;
  if (digits.length === 12 && digits.startsWith("90")) national = digits.slice(2);
  else if (digits.length === 11 && digits.startsWith("0") && !plus) national = digits.slice(1);
  else if (digits.length === 10 && !plus) national = digits;

  if (!national || !"23458".includes(national[0])) return null;
  return `+90${national}`;
}

/** +905321234567 → 532 123 45 67 (form alanında; "+90" öneki PhoneInput'ta ayrıca görünür). */
export function formatPhone(e164: string | null | undefined): string {
  if (!e164?.startsWith("+90") || e164.length !== 13) return e164 ?? "";
  const n = e164.slice(3);
  return `${n.slice(0, 3)} ${n.slice(3, 6)} ${n.slice(6, 8)} ${n.slice(8)}`;
}

export const phoneSchema = z.string().refine((v) => normalizePhone(v) !== null, PHONE_MESSAGE);

const TURKISH_MAP: Record<string, string> = {
  ş: "s",
  Ş: "s",
  ğ: "g",
  Ğ: "g",
  ı: "i",
  I: "i",
  İ: "i",
  ö: "o",
  Ö: "o",
  ü: "u",
  Ü: "u",
  ç: "c",
  Ç: "c",
};

export const RESERVED_SLUGS = new Set([
  "admin",
  "api",
  "app",
  "panel",
  "giris",
  "kayit",
  "kurulum",
  "davet",
  "destek",
  "yardim",
  "kobifabrika",
  "www",
  "mail",
  "static",
  "files",
  "r",
  "sifremi-unuttum",
  "sifre-sifirla",
  "e-posta-dogrula",
  "ayarlar",
  "test",
]);

/** İşletme adından slug (backend SlugGenerator.Generate ile aynı sonuç). */
export function toSlug(name: string): string {
  let slug = [...name.trim()]
    .map((ch) => TURKISH_MAP[ch] ?? ch)
    .join("")
    .normalize("NFD")
    .replace(/\p{Mn}/gu, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  if (slug.length > 60) slug = slug.slice(0, 60).replace(/-+$/, "");
  if (!slug) return "isletme";
  if (slug.length < 3 || RESERVED_SLUGS.has(slug)) slug = `${slug}-isletme`;
  return slug;
}

export function isValidSlug(slug: string): boolean {
  return (
    slug.length >= 3 &&
    slug.length <= 60 &&
    /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/.test(slug) &&
    !slug.includes("--") &&
    !RESERVED_SLUGS.has(slug)
  );
}

/** Panele geri dönüş adresi: sadece /panel ile başlayan iç yollar kabul edilir (açık yönlendirme yok). */
export function safeReturnPath(value: string | null | undefined): string | null {
  if (
    !value ||
    !(value.startsWith("/panel") || value.startsWith("/platform")) ||
    value.startsWith("//") ||
    value.includes("\\")
  )
    return null;
  return value;
}

// Para (backend Domain/Common/Money.cs ile aynı kurallar ve mesajlar).

export const MONEY_MAX = 999_999_999_999.99;
export const moneyMessages = {
  required: "Tutar girin.",
  positive: "Tutar 0'dan büyük olmalı.",
  decimals: "Tutar en fazla 2 ondalık basamak içerebilir.",
  max: "Tutar en fazla 999.999.999.999,99 TL olabilir.",
  invalid: "Tutarı rakamla girin (ör. 1.250,50).",
} as const;

/**
 * Kullanıcının yazdığı tutarı çözer (`lib/number-input.ts` kuralı): virgül ondalık, nokta binlik; virgülsüz "12.5" (son noktadan
 * sonra 1-2 rakam) ondalık. "1.250,50", "32.500", "1250,5", "12.5", "-300". Ondalık hane sayısı korunur ki "10,001" hatası
 * yakalansın: { value, decimals }.
 */
export function parseMoneyInput(text: string | null | undefined): { value: number; decimals: number } | null {
  return parseNumberText(text);
}

/** Kurala uymuyorsa Türkçe hata mesajı, uyuyorsa null. Boş = "Tutar girin." */
export function validateMoney(text: string | null | undefined): string | null {
  if (!text?.trim()) return moneyMessages.required;
  const parsed = parseMoneyInput(text);
  if (!parsed) return moneyMessages.invalid;
  if (parsed.value <= 0) return moneyMessages.positive;
  if (parsed.value > MONEY_MAX) return moneyMessages.max;
  if (parsed.decimals > 2) return moneyMessages.decimals;
  return null;
}

/** Zod alanı: metin girilir, API'ye sayı gider. */
export const moneySchema = z
  .string()
  .superRefine((v, ctx) => {
    const message = validateMoney(v);
    if (message) ctx.addIssue({ code: "custom", message });
  })
  .transform((v) => parseMoneyInput(v)!.value);

/** API'den gelen sayıyı form alanına: 1250.5 → "1250,50". */
export function moneyToInput(value: number): string {
  return value.toFixed(2).replace(".", ",");
}

// IBAN (backend Domain/Common/Iban.cs ile aynı).

export const IBAN_MESSAGE = "Geçerli bir TR IBAN girin (TR ile başlayan 26 karakter).";

/** Boşluk/tire atılır, büyütülür; TR + 24 rakam ve mod-97 kontrolü. Geçersizse null. */
export function normalizeIban(input: string | null | undefined): string | null {
  const compact = (input ?? "").replace(/[\s-]/g, "").toUpperCase();
  if (!/^TR\d{24}$/.test(compact)) return null;
  const rearranged = compact.slice(4) + "2927" + compact.slice(2, 4);
  return BigInt(rearranged) % BigInt(97) === BigInt(1) ? compact : null;
}

/** "TR400006200000000000000001" → "TR40 0006 2000 0000 0000 0000 01". */
export function formatIban(iban: string): string {
  return iban
    .replace(/\s/g, "")
    .replace(/(.{4})/g, "$1 ")
    .trim();
}
