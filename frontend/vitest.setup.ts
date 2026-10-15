import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => cleanup());

// findBy*/waitFor varsayılanı 1 sn; GitHub Actions'ta yük altında aşılabiliyor (Actions #18: import-wizard 1,25 sn).
configure({ asyncUtilTimeout: 5000 });

// jsdom'da olmayan tarayıcı API'leri (cmdk ve Radix kullanıyor).
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= function scrollIntoView() {};
