import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

import { colors } from "./colors";

const css = readFileSync(join(process.cwd(), "src/app/globals.css"), "utf8");

describe("colors.ts globals.css ile aynı", () => {
  it.each(Object.entries(colors))("%s", (name, hex) => {
    expect(css).toContain(`--color-${name}: ${hex};`);
  });
});
