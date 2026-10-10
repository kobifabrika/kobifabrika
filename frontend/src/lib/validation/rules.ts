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
  ş: "s", Ş: "s", ğ: "g", Ğ: "g", ı: "i", I: "i", İ: "i", ö: "o", Ö: "o", ü: "u", Ü: "u", ç: "c", Ç: "c",
};

export const RESERVED_SLUGS = new Set([
  "admin", "api", "app", "panel", "giris", "kayit", "kurulum", "davet", "destek", "yardim", "kobifabrika", "www",
  "mail", "static", "files", "r", "sifremi-unuttum", "sifre-sifirla", "e-posta-dogrula", "ayarlar", "test",
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
  if (!value || !value.startsWith("/panel") || value.startsWith("//") || value.includes("\\")) return null;
  return value;
}
