import { describe, expect, it } from "vitest";

import { composeCounter, isGsm7, measureSms, renderTemplate, unknownVariables, withFooter } from "@/lib/sms";

// Backend MarketingDomainTests / BookingDomainTests ile aynı vektörler.
describe("measureSms", () => {
  it.each([
    [0, 0],
    [1, 1],
    [160, 1],
    [161, 2],
    [306, 2],
    [307, 3],
  ])("GSM-7 %i karakter → %i SMS", (length, segments) => {
    const size = measureSms("a".repeat(length));
    expect(size.segments).toBe(segments);
    expect(size.unicode).toBe(false);
  });

  it.each([
    [70, 1],
    [71, 2],
    [134, 2],
    [135, 3],
  ])("Türkçe karakterde %i karakter → %i SMS", (length, segments) => {
    const size = measureSms("ş" + "a".repeat(length - 1));
    expect(size.unicode).toBe(true);
    expect(size.segments).toBe(segments);
  });

  it("genişletilmiş karakterler 2 sayılır", () => {
    expect(measureSms("[".repeat(80)).segments).toBe(1);
    expect(measureSms("[".repeat(81)).segments).toBe(2);
  });

  it("ö, ü, Ç GSM-7'dir; ş, ı, ğ değildir", () => {
    expect(isGsm7("Kuaför Çiçek ü")).toBe(false); // "i" sorun değil, "ç" (küçük) GSM-7 değil
    expect(isGsm7("Kuaför Ü Ö Ç")).toBe(true);
    expect(isGsm7("ı")).toBe(false);
  });
});

describe("şablon", () => {
  it("değişkenleri doldurur", () => {
    expect(renderTemplate("Merhaba {ad}, {isletme}'de bu hafta %20 indirim!", "Ayşe", "Kuaför Elif")).toBe(
      "Merhaba Ayşe, Kuaför Elif'de bu hafta %20 indirim!",
    );
  });

  it("bilinmeyen değişkenleri bulur", () => {
    expect(unknownVariables("Merhaba {ad} {soyad} {isletme} {tarih} {soyad}")).toEqual(["{soyad}", "{tarih}"]);
    expect(unknownVariables("Merhaba {ad}")).toEqual([]);
  });

  it("çıkış eki Unicode'a zorlamaz ama Türkçe gövde zorlar", () => {
    const footer = "SMS almamak icin: kobifabrika.com/c/Ab12Cd34 veya RET";
    expect(isGsm7(footer)).toBe(true);
    expect(measureSms(withFooter("Merhaba Ali, bu hafta indirim var.", footer))).toMatchObject({ unicode: false, segments: 1 });
    expect(measureSms(withFooter("Merhaba Ayşe, bu hafta indirim var.", footer))).toMatchObject({ unicode: true, segments: 2 });
  });

  it("sayaç örnek adla ve ekle hesaplar", () => {
    const c = composeCounter("Merhaba {ad}", "X", "SMS almamak icin: kobifabrika.com/c/XXXXXXXX veya RET", "Ali");
    expect(c.text).toBe("Merhaba Ali SMS almamak icin: kobifabrika.com/c/XXXXXXXX veya RET");
    expect(c.segments).toBe(1);
  });
});
