import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { BottomNav } from "./bottom-nav";

vi.mock("next/navigation", () => ({ usePathname: () => "/panel/randevular" }));

describe("BottomNav", () => {
  it("simetrik 5 düğme: Bugün, Randevu, hızlı işlem, Satış, Müşteri (Menü üst çubukta)", () => {
    renderWithSession(<BottomNav />);

    const nav = screen.getByRole("navigation", { name: "Alt menü" });
    const labels = Array.from(nav.querySelectorAll("a, button")).map(
      (el) => el.getAttribute("aria-label") ?? el.textContent?.trim(),
    );

    expect(labels).toEqual(["Bugün", "Randevu", "Hızlı işlem", "Satış", "Müşteri"]);
  });

  it("aktif sekmeyi aria-current ile işaretler", () => {
    renderWithSession(<BottomNav />);

    expect(screen.getByRole("link", { name: "Randevu" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Bugün" })).not.toHaveAttribute("aria-current");
  });

  it("Randevular modülü kapalıysa (Perakende) Randevu sekmesi yerine Ürün gelir", () => {
    renderWithSession(<BottomNav />, { modules: { appointments: false, stock: true } });

    expect(screen.queryByRole("link", { name: "Randevu" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ürün" })).toHaveAttribute("href", "/panel/urunler");
    const nav = screen.getByRole("navigation", { name: "Alt menü" });
    const labels = Array.from(nav.querySelectorAll("a, button")).map((el) => el.getAttribute("aria-label") ?? el.textContent?.trim());
    expect(labels).toEqual(["Bugün", "Ürün", "Hızlı işlem", "Satış", "Müşteri"]);
  });
});
