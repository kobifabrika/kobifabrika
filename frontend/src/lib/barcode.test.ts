import { describe, expect, it } from "vitest";

import { createScanDetector } from "./barcode";

function typeKeys(text: string, gap: number, start = 1000) {
  const detector = createScanDetector();
  let t = start;
  let result: string | null = null;
  for (const key of [...text, "Enter"]) {
    t += gap;
    result = detector.push({ key, timeStamp: t }) ?? result;
  }
  return result;
}

describe("createScanDetector", () => {
  it("recognises fast keystrokes ending with Enter as a barcode", () => {
    expect(typeKeys("8690000000011", 8)).toBe("8690000000011");
  });

  it("ignores human typing speed", () => {
    expect(typeKeys("8690000000011", 120)).toBeNull();
  });

  it("ignores too short sequences", () => {
    expect(typeKeys("12", 5)).toBeNull();
  });

  it("starts a new sequence after a pause", () => {
    const detector = createScanDetector();
    detector.push({ key: "a", timeStamp: 0 });
    detector.push({ key: "b", timeStamp: 500 });
    let t = 1000;
    for (const key of "12345") detector.push({ key, timeStamp: (t += 5) });
    expect(detector.push({ key: "Enter", timeStamp: t + 5 })).toBe("12345");
  });

  it("ignores modifier keys", () => {
    const detector = createScanDetector();
    let t = 0;
    for (const key of ["Shift", "A", "B", "C", "D"]) detector.push({ key, timeStamp: (t += 5) });
    expect(detector.push({ key: "Enter", timeStamp: t + 5 })).toBe("ABCD");
  });
});
