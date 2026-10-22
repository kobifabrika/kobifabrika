import { screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { ContactList } from "./contact-list";

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });

const items = [
  {
    id: "c1",
    displayName: "Ayşe Yılmaz",
    companyName: null,
    phone: "+905321234567",
    email: null,
    isCustomer: true,
    isSupplier: false,
    source: "Instagram",
    tags: [{ id: "t1", name: "VIP", color: "Blue" }],
    balance: 1250 as number | null,
    lastVisitAt: null,
    isArchived: false,
    createdAt: "2026-10-01T10:00:00Z",
  },
];

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    GET: (path: string) => {
      if (path === "/api/contacts") return Promise.resolve(ok({ items, total: items.length }));
      if (path === "/api/segments")
        return Promise.resolve(
          ok([{ id: null, code: "new-30", name: "Son 30 günde eklenenler", filter: null, isSystem: true }]),
        );
      return Promise.resolve(ok([]));
    },
  },
}));

describe("ContactList", () => {
  it("telefonda kartta tek dokunuşla ara ve WhatsApp linkleri var", async () => {
    renderWithSession(<ContactList />);

    const cards = await screen.findAllByRole("listitem");
    const card = cards.find((c) => within(c).queryByText("Ayşe Yılmaz"))!;
    expect(within(card).getByRole("link", { name: "Ara" })).toHaveAttribute("href", "tel:+905321234567");
    expect(within(card).getByRole("link", { name: "WhatsApp" })).toHaveAttribute("href", "https://wa.me/905321234567");
    expect(within(card).getByText("Alacak 1.250 TL")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Son 30 günde eklenenler" })).toBeInTheDocument();
  });

  it("Personel bakiye sütununu ve tutarı görmez (API de null döner)", async () => {
    items[0].balance = null;
    renderWithSession(<ContactList />, { role: "Staff" });

    await screen.findAllByText("Ayşe Yılmaz");
    expect(screen.queryByRole("columnheader", { name: "Bakiye" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Alacak/)).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Excel/ })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Yeni müşteri/ })).toBeInTheDocument();
  });

  it("Mali Müşavir ekleyemez ama Excel'e aktarır", async () => {
    items[0].balance = 1250;
    renderWithSession(<ContactList />, { role: "Accountant" });

    await screen.findAllByText("Ayşe Yılmaz");
    expect(screen.queryByRole("link", { name: /Yeni müşteri/ })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Excel/ })).toHaveAttribute(
      "href",
      expect.stringContaining("/api/contacts/export.xlsx"),
    );
  });
});
