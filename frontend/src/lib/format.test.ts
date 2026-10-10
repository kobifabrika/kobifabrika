import { describe, expect, it } from "vitest";

import { greeting } from "./format";

describe("greeting", () => {
  // İstanbul UTC+3: 05:00 UTC = 08:00, 01:00 UTC = 04:00, 02:00 UTC = 05:00 İstanbul.
  it.each([
    ["2026-10-10T05:00:00Z", "Günaydın"],
    ["2026-10-10T10:00:00Z", "İyi günler"],
    ["2026-10-10T16:30:00Z", "İyi akşamlar"],
    ["2026-10-10T21:00:00Z", "İyi geceler"],
    ["2026-10-10T01:00:00Z", "İyi geceler"],
    ["2026-10-10T02:00:00Z", "Günaydın"],
  ])("%s → %s (İstanbul saatine göre)", (iso, expected) => {
    expect(greeting(new Date(iso))).toBe(expected);
  });
});
