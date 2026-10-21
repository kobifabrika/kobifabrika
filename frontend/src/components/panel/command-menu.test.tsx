import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { CommandMenu, normalizeSearch } from "./command-menu";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const get = vi.fn();
vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: { GET: (...args: unknown[]) => get(...args) },
}));

describe("CommandMenu (Ctrl+K)", () => {
  beforeEach(() => {
    push.mockReset();
    get.mockReset();
    get.mockResolvedValue({ data: { groups: [] }, response: new Response(null, { status: 200 }) });
  });

  it("Ctrl+K ile açılır, sayfa araması Türkçe karakter duyarsızdır", async () => {
    renderWithSession(<CommandMenu />);

    fireEvent.keyDown(window, { key: "k", ctrlKey: true });
    const input = await screen.findByPlaceholderText(/sayfa ara/);
    fireEvent.change(input, { target: { value: "calisma" } });

    expect(await screen.findByRole("option", { name: /Çalışma saatleri/ })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Müşteriler/ })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("option", { name: /Çalışma saatleri/ }));
    expect(push).toHaveBeenCalledWith("/panel/ayarlar/calisma-saatleri");
  });

  it("her sayfa bir kez listelenir (aynı adres iki kez yok)", async () => {
    renderWithSession(<CommandMenu />);
    fireEvent.keyDown(window, { key: "k", ctrlKey: true });
    await screen.findByPlaceholderText(/sayfa ara/);
    expect(screen.getAllByRole("option", { name: /Yeni müşteri/ })).toHaveLength(1);
    const values = screen.getAllByRole("option").map((o) => o.getAttribute("data-value"));
    expect(new Set(values).size).toBe(values.length);
  });

  it("API araması 2 karakterden sonra çağrılır; sonuç yoksa mesaj gösterir", async () => {
    renderWithSession(<CommandMenu />);

    fireEvent.keyDown(window, { key: "k", metaKey: true });
    fireEvent.change(await screen.findByPlaceholderText(/sayfa ara/), { target: { value: "zzz" } });

    await waitFor(() => expect(get).toHaveBeenCalledWith("/api/search", { params: { query: { q: "zzz" } } }));
    expect(await screen.findByText("Sonuç bulunamadı.")).toBeInTheDocument();
  });

  it("Personel Ayarlar sayfalarını görmez", async () => {
    renderWithSession(<CommandMenu />, { role: "Staff" });

    fireEvent.keyDown(window, { key: "k", ctrlKey: true });
    fireEvent.change(await screen.findByPlaceholderText(/sayfa ara/), { target: { value: "ayar" } });

    expect(screen.queryByRole("option", { name: /Ayarlar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Çalışma saatleri/ })).not.toBeInTheDocument();
  });

  it("normalizeSearch Türkçe büyük/küçük harf ve aksanları eşitler", () => {
    expect(normalizeSearch("ÇALIŞMA İzin")).toBe("calisma izin");
  });
});
