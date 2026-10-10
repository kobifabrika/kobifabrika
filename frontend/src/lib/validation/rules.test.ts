import { describe, expect, it } from "vitest";

import { formatPhone, isValidSlug, normalizePhone, passwordSchema, safeReturnPath, toSlug } from "./rules";

// Örnekler backend testleriyle aynı (SlugGeneratorTests, PhoneNumberTests).
describe("slug", () => {
  it.each([
    ["Şık Kuaför & Güzellik", "sik-kuafor-guzellik"],
    ["IŞIK Oto Servis", "isik-oto-servis"],
    ["İstanbul Çiçekçilik", "istanbul-cicekcilik"],
    ["Ağaç Ev Kafe", "agac-ev-kafe"],
    ["  Öz-Üretim   Ltd. Şti. ", "oz-uretim-ltd-sti"],
    ["Café Rêve", "cafe-reve"],
    ["Panel", "panel-isletme"],
    ["Ak", "ak-isletme"],
    ["!!!", "isletme"],
  ])("%s → %s", (name, slug) => {
    expect(toSlug(name)).toBe(slug);
  });

  it("elle girilen slug'ı doğrular", () => {
    expect(isValidSlug("sik-kuafor")).toBe(true);
    expect(isValidSlug("Sik-Kuafor")).toBe(false);
    expect(isValidSlug("sik--kuafor")).toBe(false);
    expect(isValidSlug("şık")).toBe(false);
    expect(isValidSlug("panel")).toBe(false);
    expect(isValidSlug("ab")).toBe(false);
  });
});

describe("telefon", () => {
  it.each([
    ["0532 123 45 67", "+905321234567"],
    ["532-123-4567", "+905321234567"],
    ["+90 532 123 45 67", "+905321234567"],
    ["(0212) 123 45 67", "+902121234567"],
    ["0850 123 45 67", "+908501234567"],
  ])("%s → %s", (input, e164) => {
    expect(normalizePhone(input)).toBe(e164);
  });

  it.each(["", "12345", "0632 123 45 67", "+1 532 123 45 67", "0532 123 45 6a", "532+1234567"])(
    "%s geçersiz",
    (input) => {
      expect(normalizePhone(input)).toBeNull();
    },
  );

  it("E.164'ü +90 önekli kutu için okunur biçime çevirir", () => {
    expect(formatPhone("+905321234567")).toBe("532 123 45 67");
    expect(normalizePhone(formatPhone("+902125554433"))).toBe("+902125554433");
  });
});

describe("şifre", () => {
  it("en az 8 karakter, bir harf ve bir rakam ister; mesajlar Türkçe", () => {
    expect(passwordSchema.safeParse("Sifre1234").success).toBe(true);
    expect(passwordSchema.safeParse("şifre123").success).toBe(true);
    expect(passwordSchema.safeParse("kisa1").error?.issues[0].message).toBe("Şifre en az 8 karakter olmalı.");
    expect(passwordSchema.safeParse("12345678").error?.issues[0].message).toBe("Şifre en az bir harf içermeli.");
    expect(passwordSchema.safeParse("sadeceharf").error?.issues[0].message).toBe("Şifre en az bir rakam içermeli.");
  });
});

describe("geri dönüş adresi", () => {
  it("sadece /panel ile başlayan iç yolları kabul eder", () => {
    expect(safeReturnPath("/panel/ayarlar")).toBe("/panel/ayarlar");
    expect(safeReturnPath("https://kotu.com")).toBeNull();
    expect(safeReturnPath("//kotu.com/panel")).toBeNull();
    expect(safeReturnPath("/kurulum")).toBeNull();
    expect(safeReturnPath(null)).toBeNull();
  });
});
