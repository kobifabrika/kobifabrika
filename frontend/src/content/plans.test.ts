import { describe, expect, it } from "vitest";

import { formatPlanPrice, plans, type Plan } from "./plans";

describe("paketler (src/content/plans.ts)", () => {
  it("Çırak, Kalfa, Usta sırasıyla; Çırak ücretsiz", () => {
    expect(plans.map((p) => p.name)).toEqual(["Çırak", "Kalfa", "Usta"]);
    expect(formatPlanPrice(plans[0])).toBe("Ücretsiz");
  });

  it("fiyatı belli olmayan paket 'Lansman fiyatı yakında' gösterir, fiyat girilince TL biçimlenir", () => {
    const plan: Plan = { ...plans[1], monthlyPrice: null };
    expect(formatPlanPrice(plan)).toBe("Lansman fiyatı yakında");
    expect(formatPlanPrice({ ...plan, monthlyPrice: 1299 })).toBe("1.299 TL");
  });
});
