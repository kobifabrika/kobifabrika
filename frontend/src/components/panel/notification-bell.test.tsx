import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { NotificationBell } from "./notification-bell";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

let unread = 2;
const items = [
  { id: "n1", type: "invitation.accepted", title: "Ayşe ekibe katıldı", body: null, link: "/panel/ayarlar/ekip", createdAt: new Date().toISOString(), readAt: null as string | null },
  { id: "n2", type: "team.joined", title: "Hoş geldin", body: null, link: null, createdAt: new Date().toISOString(), readAt: null as string | null },
];
const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    GET: (path: string) =>
      Promise.resolve(path === "/api/notifications/unread-count" ? ok({ count: unread }) : ok({ items, total: items.length, unread })),
    POST: (path: string, init?: { params: { path: { id: string } } }) => {
      if (path === "/api/notifications/read-all") unread = 0;
      else {
        unread -= 1;
        items.find((i) => i.id === init?.params.path.id)!.readAt = new Date().toISOString();
      }
      return Promise.resolve({ data: undefined, response: new Response(null, { status: 204 }) });
    },
  },
}));

describe("NotificationBell", () => {
  beforeEach(() => {
    unread = 2;
    items.forEach((i) => (i.readAt = null));
    push.mockReset();
  });

  it("okunmamış sayısını gösterir; bildirime dokununca okundu olur ve linke gider", async () => {
    renderWithSession(<NotificationBell />);

    const bell = await screen.findByRole("button", { name: "Bildirimler, 2 okunmamış" });
    fireEvent.click(bell);
    fireEvent.click(await screen.findByText("Ayşe ekibe katıldı"));

    expect(push).toHaveBeenCalledWith("/panel/ayarlar/ekip");
    await waitFor(() => expect(screen.getByRole("button", { name: "Bildirimler, 1 okunmamış" })).toBeInTheDocument());
  });

  it("tümünü okundu işaretleyince rozet kaybolur", async () => {
    renderWithSession(<NotificationBell />);

    fireEvent.click(await screen.findByRole("button", { name: "Bildirimler, 2 okunmamış" }));
    fireEvent.click(await screen.findByRole("button", { name: "Tümünü okundu işaretle" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Bildirimler" })).toBeInTheDocument());
  });
});
