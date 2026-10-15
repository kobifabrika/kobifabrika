import { describe, expect, it } from "vitest";

import { siteSlogan, siteSocial } from "./site";

describe("site içeriği", () => {
  it("slogan KOBİ'nin dijital fabrikası", () => {
    expect(siteSlogan).toBe("KOBİ'nin dijital fabrikası");
  });

  it("sosyal medya hesapları alt bilgide ikonlarıyla", () => {
    expect(siteSocial.map((s) => s.href)).toEqual([
      "https://www.instagram.com/kobifabrika",
      "https://www.facebook.com/kobifabrika",
      "https://www.youtube.com/@kobifabrika",
      "https://www.tiktok.com/@kobifabrika",
    ]);
  });
});
