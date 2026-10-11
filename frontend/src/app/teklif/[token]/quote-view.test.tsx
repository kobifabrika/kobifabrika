import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { ApiError } from "@/lib/api/client";

import { QuoteView } from "./quote-view";

vi.mock("next/navigation", () => ({ useParams: () => ({ token: "tok" }) }));

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });
const quote = {
  businessName: "Moda Butik",
  logoUrl: null,
  businessPhone: "0212 555 44 33",
  businessEmail: null,
  businessAddress: null,
  contactName: "Ayşe Yılmaz",
  no: "TKL-2026-0004",
  date: "2026-10-11",
  validUntil: "2026-10-26",
  status: "Sent",
  canRespond: true,
  lines: [
    {
      id: "l1",
      productId: null,
      description: "Mutfak dolabı",
      unit: "Piece",
      quantity: 2,
      unitPrice: 5000,
      lineDiscount: 0,
      allocatedDiscount: 0,
      vatRate: 20,
      vatAmount: 1666.67,
      lineTotal: 10000,
    },
  ],
  subTotal: 10000,
  lineDiscountTotal: 0,
  discountTotal: 0,
  vatTotal: 1666.67,
  total: 10000,
  vat: [{ rate: 20, base: 8333.33, vat: 1666.67 }],
  notes: "Montaj dahildir.",
  respondedAt: null,
  responseNote: null,
};
const respond = vi.fn();
let getResult: () => Promise<unknown> = () => Promise.resolve(ok(quote));

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    GET: () => getResult(),
    POST: (path: string, init: { body: { note: string | null } }) => {
      respond(path, init.body);
      const status = path.endsWith("accept") ? "Accepted" : "Rejected";
      return Promise.resolve(ok({ ...quote, status, canRespond: false, responseNote: init.body.note }));
    },
  },
}));

function renderView() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <QuoteView />
    </QueryClientProvider>,
  );
}

describe("QuoteView", () => {
  it("shows the quote and accepts it", async () => {
    getResult = () => Promise.resolve(ok(quote));
    renderView();
    expect(await screen.findByText("Sayın Ayşe Yılmaz,")).toBeInTheDocument();
    expect(screen.getByText("Mutfak dolabı")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Teklifi onayla/ }));
    expect(await screen.findByText(/Teklifi onayladınız/)).toBeInTheDocument();
    expect(respond).toHaveBeenCalledWith("/api/public/quotes/{token}/accept", { note: null });
  });

  it("rejects with an optional note", async () => {
    getResult = () => Promise.resolve(ok(quote));
    renderView();
    fireEvent.click(await screen.findByRole("button", { name: "Reddet" }));
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "Fiyat yüksek" } });
    fireEvent.click(screen.getByRole("button", { name: /Reddi gönder/ }));
    expect(await screen.findByText(/Teklifi reddettiniz/)).toBeInTheDocument();
    expect(respond).toHaveBeenLastCalledWith("/api/public/quotes/{token}/reject", { note: "Fiyat yüksek" });
  });

  it("explains an expired link", async () => {
    getResult = () => Promise.reject(new ApiError(410, { detail: "Bu teklifin süresi doldu." }));
    renderView();
    expect(await screen.findByText("Bu teklifin süresi doldu")).toBeInTheDocument();
  });
});
