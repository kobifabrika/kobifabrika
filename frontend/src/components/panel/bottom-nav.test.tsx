import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { BottomNav } from "./bottom-nav";

vi.mock("next/navigation", () => ({ usePathname: () => "/panel/randevular" }));

describe("BottomNav", () => {
  it("Bugün, Randevu, hızlı işlem, Satış, Müşteri ve Menü sekmelerini sırayla gösterir", () => {
    render(<BottomNav />);

    const nav = screen.getByRole("navigation", { name: "Alt menü" });
    const labels = Array.from(nav.querySelectorAll("a, button")).map(
      (el) => el.getAttribute("aria-label") ?? el.textContent?.trim(),
    );

    expect(labels).toEqual(["Bugün", "Randevu", "Hızlı işlem", "Satış", "Müşteri", "Menü"]);
  });

  it("aktif sekmeyi aria-current ile işaretler", () => {
    render(<BottomNav />);

    expect(screen.getByRole("link", { name: "Randevu" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Bugün" })).not.toHaveAttribute("aria-current");
  });
});
