import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { defaultDueDate } from "@/lib/cart";
import { todayIstanbul } from "@/lib/format";
import type { Quote } from "@/lib/api/types";
import { renderWithSession } from "@/test/session";

import { ConvertDialog } from "./quote-detail";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), back: vi.fn() }) }));

const post = vi.fn();

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    GET: () => Promise.resolve({ data: [], response: new Response(null, { status: 200 }) }),
    POST: (path: string, init: unknown) => {
      post(path, init);
      return Promise.resolve({ data: { sale: { id: "s1", no: "STS-2026-0001" }, warnings: [] }, response: new Response(null, { status: 201 }) });
    },
  },
}));

const quote = {
  id: "q1",
  no: "TKL-2026-0001",
  contactName: "Ayşe Yılmaz",
  subtotal: 1000,
  lineDiscountTotal: 0,
  discountTotal: 0,
  vatTotal: 200,
  total: 1200,
  vat: [],
} as unknown as Quote;

beforeEach(() => post.mockClear());

describe("Teklifi satışa dönüştür", () => {
  it("veresiye seçiliyken vade alanını varsayılan tarihle gösterir ve seçilen vadeyi gönderir", async () => {
    renderWithSession(<ConvertDialog quote={quote} onClose={() => undefined} />);

    const due = screen.getByLabelText("Vade");
    expect(due).toHaveValue(defaultDueDate(todayIstanbul(), 30));

    fireEvent.change(due, { target: { value: "2099-01-15" } });
    fireEvent.click(screen.getByRole("button", { name: /Satışı kaydet/ }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/quotes/{id}/convert",
      expect.objectContaining({ body: expect.objectContaining({ dueDate: "2099-01-15" }) }),
    );
  });

  it("geçmiş vadeyi kaydetmez; nakit seçilince vade alanı kaybolur ve vade gönderilmez", async () => {
    renderWithSession(<ConvertDialog quote={quote} onClose={() => undefined} />);

    fireEvent.change(screen.getByLabelText("Vade"), { target: { value: "2000-01-01" } });
    fireEvent.click(screen.getByRole("button", { name: /Satışı kaydet/ }));
    expect((await screen.findAllByText("Vade tarihi satış tarihinden önce olamaz.")).length).toBeGreaterThan(0);
    expect(post).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("radio", { name: /Nakit/ }));
    expect(screen.queryByLabelText("Vade")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Satışı kaydet/ }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/quotes/{id}/convert",
      expect.objectContaining({ body: expect.objectContaining({ dueDate: null }) }),
    );
  });
});
