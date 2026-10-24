import type { ErrorEvent } from "@sentry/nextjs";
import { describe, expect, it } from "vitest";
import { maskPersonalData, scrubBreadcrumb, scrubEvent } from "./sentry-scrub";

describe("maskPersonalData", () => {
  it.each([
    ["Gönderilemedi: ayse.yilmaz@ornek.com.tr", "Gönderilemedi: ***@ornek.com.tr"],
    ["tel +905321234567 hatalı", "tel [telefon **67] hatalı"],
    ["tel +90 532 123 45 67", "tel [telefon **67]"],
    ["0532 123 4567 aranacak", "[telefon **67] aranacak"],
    ["+4915123456789", "[telefon **89]"],
    ["Sipariş 2026-10-24 tutar 1250,50", "Sipariş 2026-10-24 tutar 1250,50"],
  ])("%s", (input, expected) => {
    expect(maskPersonalData(input)).toBe(expected);
  });
});

describe("scrubEvent", () => {
  it("kullanıcıyı, çerezleri ve başlıkları siler; metinleri maskeler", () => {
    const event = {
      type: undefined,
      message: "Kayıt: ali@ornek.com",
      user: { email: "ali@ornek.com", ip_address: "31.223.77.108" },
      exception: { values: [{ type: "Error", value: "05321234567 bulunamadı" }] },
      request: {
        url: "https://kobifabrika.com/r/isiltı?email=ali@ornek.com",
        query_string: "email=ali@ornek.com",
        cookies: { kf: "x" },
        headers: { "x-forwarded-for": "31.223.77.108" },
      },
      extra: { to: "veli@ornek.com", count: 2 },
      breadcrumbs: [{ message: "Giriş: ali@ornek.com" }],
    } as ErrorEvent;

    const scrubbed = scrubEvent(event);

    expect(scrubbed.user).toBeUndefined();
    expect(scrubbed.message).toBe("Kayıt: ***@ornek.com");
    expect(scrubbed.exception?.values?.[0].value).toBe("[telefon **67] bulunamadı");
    expect(scrubbed.request?.cookies).toBeUndefined();
    expect(scrubbed.request?.headers).toBeUndefined();
    expect(scrubbed.request?.url).toBe("https://kobifabrika.com/r/isiltı?email=***@ornek.com");
    expect(scrubbed.request?.query_string).toBe("email=***@ornek.com");
    expect(scrubbed.extra).toEqual({ to: "***@ornek.com", count: 2 });
    expect(scrubbed.breadcrumbs?.[0].message).toBe("Giriş: ***@ornek.com");
  });

  it("kayıt izindeki veriyi maskeler", () => {
    expect(scrubBreadcrumb({ data: { tel: "+905321234567" } }).data).toEqual({ tel: "[telefon **67]" });
  });
});
