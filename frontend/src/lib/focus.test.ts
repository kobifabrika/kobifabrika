import { afterEach, describe, expect, it, vi } from "vitest";

import { phoneSafeOpenAutoFocus } from "./focus";

function openEvent() {
  const container = document.createElement("div");
  container.tabIndex = -1;
  document.body.append(container);
  const event = new Event("focusScope.autoFocusOnMount", { cancelable: true });
  container.addEventListener(event.type, phoneSafeOpenAutoFocus);
  container.dispatchEvent(event);
  return { event, container };
}

const media = (desktop: boolean) =>
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: desktop && query.includes("1024"), media: query }) as MediaQueryList);

describe("pencere açılış odağı", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    document.body.innerHTML = "";
  });

  it("telefonda ilk alana odaklanılmaz, odak pencerede kalır (klavye açılmaz)", () => {
    media(false);
    const { event, container } = openEvent();
    expect(event.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(container);
  });

  it("masaüstünde varsayılan davranış (ilk alan) sürer", () => {
    media(true);
    const { event } = openEvent();
    expect(event.defaultPrevented).toBe(false);
  });
});
