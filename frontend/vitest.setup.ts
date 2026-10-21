import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => cleanup());

// findBy*/waitFor varsayılanı 1 sn; GitHub Actions ve yerel tam koşuda yük altında aşılabiliyor (Actions #18; 2026-10-21 import-wizard 5 sn).
configure({ asyncUtilTimeout: 10_000 });

// jsdom'da olmayan tarayıcı API'leri (cmdk ve Radix kullanıyor).
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= function scrollIntoView() {};

// Konsolda hata/uyarı → test düşer (2026-10-21): React'in geliştirme uyarıları (aynı key, kontrolsüz → kontrollü input, iç içe
// geçersiz eleman) sadece geliştirme derlemesinde çıkar; E2E production derlemesine karşı koştuğu için bunları ancak burada görürüz.
// Beklenen bir konsol çıktısı varsa test kendi içinde `vi.spyOn(console, "error").mockImplementation(...)` ile susturur.
const consoleIssues: string[] = [];
// jsdom'da yerleşim yok: grafik kapsayıcısının boyutu 0 (tarayıcıda olmaz).
const jsdomOnly = [/The width\(0\) and height\(0\) of chart should be greater than 0/];
for (const kind of ["error", "warn"] as const) {
  const original = console[kind].bind(console);
  console[kind] = (...args: unknown[]) => {
    const text = args.map((a) => (a instanceof Error ? a.message : String(a))).join(" ");
    if (!jsdomOnly.some((r) => r.test(text))) consoleIssues.push(`console.${kind}: ${text.slice(0, 500)}`);
    original(...args);
  };
}
afterEach(() => {
  const issues = consoleIssues.splice(0);
  if (issues.length > 0) throw new Error(`Konsolda ${issues.length} hata/uyarı:\n- ${issues.join("\n- ")}`);
});
