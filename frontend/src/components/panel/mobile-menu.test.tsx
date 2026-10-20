import { fireEvent, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { MobileMenu } from "./mobile-menu";

vi.mock("next/navigation", () => ({ usePathname: () => "/panel", useRouter: () => ({ push: vi.fn(), refresh: vi.fn() }) }));

describe("MobileMenu", () => {
  it("sağ üstteki Menü kullanıcı bölümü ve ana menüyü birlikte açar", async () => {
    renderWithSession(<MobileMenu />);
    fireEvent.click(screen.getByRole("button", { name: "Menü" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Emir Kaya")).toBeInTheDocument();
    expect(within(dialog).getByRole("link", { name: "Hesabım" })).toHaveAttribute("href", "/panel/hesabim");
    expect(within(dialog).getByRole("button", { name: "Çıkış yap" })).toBeInTheDocument();
    expect(within(dialog).getByRole("navigation", { name: "Ana menü" })).toBeInTheDocument();
    expect(within(dialog).queryByRole("link", { name: /Süper admin/ })).not.toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /Geri bildirim gönder/ })).toBeInTheDocument();
  });

  it("platform yöneticisine süper admin linkini gösterir", async () => {
    renderWithSession(<MobileMenu />, { platformAdmin: true });
    fireEvent.click(screen.getByRole("button", { name: "Menü" }));
    expect(await screen.findByRole("link", { name: /Süper admin/ })).toHaveAttribute("href", "/platform");
  });
});
