import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { Chip, ScrollRow, TabNav } from "./scroll-row";

describe("ScrollRow", () => {
  it("kaydırma çubuğu gizli, son öğenin sağında iç boşluk var", () => {
    render(
      <ScrollRow role="group" aria-label="Süzgeç">
        <Chip active onClick={vi.fn()}>
          Tümü
        </Chip>
        <Chip active={false}>Kritik</Chip>
      </ScrollRow>,
    );
    const row = screen.getByRole("group", { name: "Süzgeç" });
    expect(row).toHaveClass("no-scrollbar", "overflow-x-auto", "pl-4");
    expect(row.firstElementChild).toHaveClass("w-max", "pr-4");
    expect(screen.getByRole("button", { name: "Tümü" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Kritik" })).toHaveAttribute("aria-pressed", "false");
  });

  it("TabNav aktif sekmeyi işaretler", () => {
    render(
      <TabNav
        label="Sekmeler"
        active="b"
        tabs={[
          { key: "a", label: "A", href: "?sekme=a" },
          { key: "b", label: "B", href: "?sekme=b" },
        ]}
      />,
    );
    const nav = screen.getByRole("navigation", { name: "Sekmeler" });
    expect(nav).toHaveClass("no-scrollbar");
    expect(screen.getByRole("link", { name: "B" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "A" })).not.toHaveAttribute("aria-current");
  });
});
