import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithSession, testMe } from "@/test/session";

import { BusinessSwitcher } from "./business-switcher";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const post = vi.fn();
vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: { POST: (...args: unknown[]) => post(...args) },
}));

const longName = "Şık Kuaför Güzellik ve Bakım Salonu Kadıköy Moda Şubesi";

describe("BusinessSwitcher", () => {
  beforeEach(() => {
    push.mockReset();
    post.mockReset();
  });

  it("tek üyelikte işletme adını tek satırda kısaltarak gösterir; tamamı title'da", () => {
    renderWithSession(<BusinessSwitcher variant="sidebar" />, { businessName: longName });

    const name = screen.getByText(longName);
    expect(name).toHaveClass("truncate");
    expect(screen.getByTestId("business-name")).toHaveAttribute("title", longName);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("logo varsa küçük logoyu gösterir", () => {
    const { container } = renderWithSession(<BusinessSwitcher variant="topbar" />, { logoUrl: "/files/logo.png" });

    expect(container.querySelector("img")).toHaveAttribute("src", expect.stringContaining("/files/logo.png"));
  });

  it("logo yoksa resim göstermez", () => {
    const { container } = renderWithSession(<BusinessSwitcher variant="topbar" />);

    expect(container.querySelector("img")).toBeNull();
  });

  it("birden fazla üyelikte ada tıklayınca işletme değiştirme menüsü açılır ve seçilen işletmeye geçer", async () => {
    const other = { businessId: "b2", businessName: "Moda Butik", role: "Manager" as const };
    post.mockResolvedValue({
      data: { ...testMe(), business: { ...testMe().business!, id: "b2", name: "Moda Butik" } },
      response: new Response(null, { status: 200 }),
    });
    renderWithSession(<BusinessSwitcher variant="sidebar" />, { otherMemberships: [other] });

    const trigger = screen.getByRole("button", { name: "İşletme: Şık Kuaför. İşletme değiştir" });
    expect(trigger).toHaveAttribute("title", "Şık Kuaför");
    fireEvent.keyDown(trigger, { key: "Enter" });

    expect(await screen.findByText("İşletme değiştir")).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /Şık Kuaför/ })).toHaveAttribute("data-disabled");
    fireEvent.click(screen.getByRole("menuitem", { name: /Moda Butik/ }));

    await waitFor(() =>
      expect(post).toHaveBeenCalledWith("/api/auth/switch-business", { body: { businessId: "b2" } }),
    );
    await waitFor(() => expect(push).toHaveBeenCalledWith("/panel"));
  });
});
