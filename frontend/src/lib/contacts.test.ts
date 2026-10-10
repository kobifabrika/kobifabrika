import { describe, expect, it } from "vitest";

import { displayPhone, telLink, waLink } from "./contacts";

describe("iletişim linkleri", () => {
  it("tel: ve wa.me (numara + olmadan, mesaj kodlanır)", () => {
    expect(telLink("+905321234567")).toBe("tel:+905321234567");
    expect(waLink("+905321234567")).toBe("https://wa.me/905321234567");
    expect(waLink("+905321234567", "Merhaba Ayşe & ekip")).toBe(
      "https://wa.me/905321234567?text=Merhaba%20Ay%C5%9Fe%20%26%20ekip",
    );
  });

  it("telefonu 0'lı yerel biçimde gösterir", () => {
    expect(displayPhone("+905321234567")).toBe("0532 123 45 67");
    expect(displayPhone(null)).toBe("");
  });
});
