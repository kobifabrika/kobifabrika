import { describe, expect, it } from "vitest";

import { formatPlanPrice, planLimits, planPriceNote, plans, signupHref } from "./plans";

const [cirak, kalfa, usta] = plans;

describe("paketler (src/content/plans.ts, karar 2026-10-17)", () => {
  it("Çırak, Kalfa, Usta sırasıyla; Çırak ücretsiz ve online randevu içermez", () => {
    expect(plans.map((p) => p.name)).toEqual(["Çırak", "Kalfa", "Usta"]);
    expect(formatPlanPrice(cirak!)).toBe("Ücretsiz");
    expect(cirak!.features.join(" ")).not.toMatch(/online randevu/i);
    expect(kalfa!.features.join(" ")).toMatch(/Online randevu/);
  });

  it("onaylı fiyatlar: aylık ve yıllıkta ayda düşen", () => {
    expect(formatPlanPrice(kalfa!, "aylik")).toBe("699 TL");
    expect(formatPlanPrice(kalfa!, "yillik")).toBe("583 TL");
    expect(planPriceNote(kalfa!, "yillik")).toBe("Yılda 6.990 TL (2 ay hediye)");
    expect(formatPlanPrice(usta!, "aylik")).toBe("1.299 TL");
    expect(planPriceNote(usta!, "yillik")).toBe("Yılda 12.990 TL (2 ay hediye)");
  });

  it("limitler", () => {
    expect(planLimits(cirak!)).toBe("1 kullanıcı · 100 müşteri · ayda 50 randevu");
    expect(planLimits(kalfa!)).toBe("3 kullanıcı · 1.000 müşteri · ayda 500 randevu");
    expect(planLimits(usta!)).toBe("10 kullanıcı · sınırsız müşteri · sınırsız randevu");
  });

  it("kayıt linki paket ve dönemi taşır", () => {
    expect(signupHref(kalfa!, "yillik")).toBe("/kayit?paket=kalfa&donem=yillik");
    expect(signupHref(cirak!, "aylik")).toBe("/kayit?paket=cirak");
  });
});
