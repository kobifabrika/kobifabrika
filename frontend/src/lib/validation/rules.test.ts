import { describe, expect, it } from "vitest";

import {
  formatIban,
  formatPhone,
  isValidSlug,
  moneySchema,
  moneyToInput,
  normalizeIban,
  normalizePhone,
  parseMoneyInput,
  passwordSchema,
  safeReturnPath,
  toSlug,
  validateMoney,
} from "./rules";

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

// Backend tests/KobiFabrika.Tests/Domain/IbanAndMoneyTests.cs ile aynı örnekler.
describe("para kuralları", () => {
  it.each([
    ["0,01", null],
    ["1250", null],
    ["1.250,50", null],
    ["1250.5", null],
    ["999.999.999.999,99", null],
    ["", "Tutar girin."],
    ["0", "Tutar 0'dan büyük olmalı."],
    ["-5", "Tutar 0'dan büyük olmalı."],
    ["10,001", "Tutar en fazla 2 ondalık basamak içerebilir."],
    ["1.000.000.000.000", "Tutar en fazla 999.999.999.999,99 TL olabilir."],
    ["on lira", "Tutarı rakamla girin (ör. 1.250,50)."],
  ])("%s → %s", (input, expected) => {
    expect(validateMoney(input)).toBe(expected);
  });

  it("Türkçe ve noktalı yazımı aynı sayıya çevirir", () => {
    expect(parseMoneyInput("1.250,50")?.value).toBe(1250.5);
    expect(parseMoneyInput("1250.50")?.value).toBe(1250.5);
    expect(parseMoneyInput("1.250")?.value).toBe(1250);
    expect(parseMoneyInput("1.250,50 TL")?.value).toBe(1250.5);
    expect(moneySchema.parse("99,90")).toBe(99.9);
    expect(moneyToInput(1250.5)).toBe("1250,50");
  });
});

describe("IBAN", () => {
  it.each([
    ["TR400006200000000000000001", "TR400006200000000000000001"],
    ["tr83 0001 0000 0000 0123 4567 89", "TR830001000000000123456789"],
    [" TR10-0004-6000-0888-0000-0123-45 ", "TR100004600008880000012345"],
  ])("geçerli: %s", (input, expected) => {
    expect(normalizeIban(input)).toBe(expected);
  });

  it.each([
    "",
    "TR410006200000000000000001",
    "TR40000620000000000000000",
    "DE89370400440532013000",
    "TR40000620000000000000000A",
  ])("geçersiz: %s", (input) => {
    expect(normalizeIban(input)).toBeNull();
  });

  it("4'erli gruplar", () => {
    expect(formatIban("TR400006200000000000000001")).toBe("TR40 0006 2000 0000 0000 0000 01");
  });
});
