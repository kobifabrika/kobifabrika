import { describe, expect, it } from "vitest";

import { dayOf, gridBounds, hhmm, layoutBlocks, minutesOf, parseHhmm, snap, toIso } from "./calendar";

describe("calendar", () => {
  it("converts between istanbul minutes and utc", () => {
    expect(parseHhmm("14:30")).toBe(870);
    expect(hhmm(870)).toBe("14:30");
    expect(hhmm(1440)).toBe("24:00");
    expect(toIso("2026-10-14", 600)).toBe("2026-10-14T07:00:00.000Z");
    expect(minutesOf("2026-10-14T07:00:00.000Z")).toBe(600);
    // İstanbul 00:30 önceki UTC gününe düşer.
    expect(dayOf("2026-10-13T21:30:00.000Z")).toBe("2026-10-14");
    expect(minutesOf("2026-10-13T21:30:00.000Z")).toBe(30);
  });

  it("snaps to the step", () => {
    expect(snap(607, 15)).toBe(600);
    expect(snap(608, 15)).toBe(615);
    expect(snap(-8, 15)).toBe(-15);
  });

  it("puts overlapping blocks side by side", () => {
    const placed = layoutBlocks([
      { id: "a", start: 600, end: 660 },
      { id: "b", start: 630, end: 690 },
      { id: "c", start: 660, end: 700 },
      { id: "d", start: 720, end: 760 },
    ]);
    const byId = Object.fromEntries(placed.map((p) => [p.id, p]));
    expect(byId.a).toMatchObject({ lane: 0, lanes: 2 });
    expect(byId.b).toMatchObject({ lane: 1, lanes: 2 });
    expect(byId.c).toMatchObject({ lane: 0, lanes: 2 }); // a bitince boşalan şerit
    expect(byId.d).toMatchObject({ lane: 0, lanes: 1 }); // ayrı küme
  });

  it("back to back blocks do not overlap", () => {
    const placed = layoutBlocks([
      { id: "a", start: 600, end: 645 },
      { id: "b", start: 645, end: 690 },
    ]);
    expect(placed.every((p) => p.lanes === 1)).toBe(true);
  });

  it("grid bounds cover open hours and outside appointments", () => {
    expect(gridBounds([{ opens: "09:30", closes: "18:15" }, { opens: null, closes: null }])).toEqual({ start: 540, end: 1140 });
    expect(gridBounds([], [])).toEqual({ start: 540, end: 1140 });
    expect(gridBounds([{ opens: "09:00", closes: "19:00" }], [{ start: 1200, end: 1260 }])).toEqual({ start: 540, end: 1260 });
    expect(gridBounds([{ opens: "10:00", closes: "23:59" }])).toEqual({ start: 600, end: 1440 });
  });
});
