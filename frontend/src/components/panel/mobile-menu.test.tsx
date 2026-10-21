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

  it("başlıkta işletmenin logosu ve adı, KobiFabrika logosu en altta", async () => {
    renderWithSession(<MobileMenu />, { logoUrl: "/files/logo.png" });
    fireEvent.click(screen.getByRole("button", { name: "Menü" }));
    const dialog = await screen.findByRole("dialog", { name: "Şık Kuaför" });
    const brand = within(dialog).getByRole("img", { name: "KobiFabrika" });
    expect(brand).toHaveAttribute("src", "/marka/logo-koyu.svg");
    const nav = within(dialog).getByRole("navigation", { name: "Ana menü" });
    // Belge sırasında KobiFabrika logosu ana menüden sonra gelir.
    expect(nav.compareDocumentPosition(brand) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(dialog.querySelector('img[src$="/files/logo.png"]')).not.toBeNull();
  });

  it("logo yoksa başlıkta sadece işletme adı", async () => {
    renderWithSession(<MobileMenu />);
    fireEvent.click(screen.getByRole("button", { name: "Menü" }));
    const dialog = await screen.findByRole("dialog", { name: "Şık Kuaför" });
    expect(dialog.querySelectorAll("img")).toHaveLength(1);
  });

  it("platform yöneticisine süper admin linkini gösterir", async () => {
    renderWithSession(<MobileMenu />, { platformAdmin: true });
    fireEvent.click(screen.getByRole("button", { name: "Menü" }));
    expect(await screen.findByRole("link", { name: /Süper admin/ })).toHaveAttribute("href", "/platform");
  });
});
