import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { RECEIPT_FOOTER, ReceiptView } from "./receipt-view";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams() }));

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });
const longName = "Organik soğuk sıkım natürel sızma zeytinyağı teneke kutu 5 litre";
const sale = {
  id: "s1",
  no: "SAT-2026-000001",
  date: "2026-10-11T09:30:00Z",
  contactId: null,
  contactName: null,
  contactPhone: null,
  status: "Completed",
  subTotal: 3703.68,
  lineDiscountTotal: 0,
  discountTotal: 0,
  discount: null,
  vatTotal: 617.28,
  total: 3703.68,
  vat: [{ rate: 20, base: 3086.4, vat: 617.28 }],
  lines: [
    {
      id: "l1",
      productId: null,
      description: longName,
      unit: "Piece",
      quantity: 3,
      unitPrice: 1234.56,
      lineDiscount: 0,
      discount: null,
      allocatedDiscount: 0,
      vatRate: 20,
      vatAmount: 617.28,
      lineTotal: 3703.68,
    },
  ],
  payments: [{ id: "p1", method: "Cash", amount: 3703.68, cashAccountId: null, cashAccountName: "Nakit Kasa" }],
  note: null,
  createdBy: "u1",
  createdByName: "Emir Kaya",
  cancelledAt: null,
  cancelledByName: null,
  cancelReason: null,
  quoteId: null,
  quoteNo: null,
};

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: { GET: () => Promise.resolve(ok(sale)) },
}));

describe("ReceiptView", () => {
  it("80 mm varsayılan; alt yazı mali değer olmadığını söyler", async () => {
    renderWithSession(<ReceiptView id="s1" />);
    const receipt = await screen.findByRole("article", { name: "Fiş SAT-2026-000001" });
    expect(receipt).toHaveAttribute("data-width", "80");
    expect(receipt.className).toContain("w-[80mm]");
    expect(screen.getByText(RECEIPT_FOOTER)).toBeInTheDocument();
  });

  it("58 mm'de dar basılır, uzun ürün adı kesilmeden alt satıra kayar", async () => {
    renderWithSession(<ReceiptView id="s1" />, { receiptWidth: 58 });
    const receipt = await screen.findByRole("article", { name: "Fiş SAT-2026-000001" });
    expect(receipt).toHaveAttribute("data-width", "58");
    expect(receipt.className).toContain("w-[58mm]");
    expect(receipt.className).toContain("wrap-anywhere");
    const name = screen.getByText(longName);
    expect(name.className).not.toMatch(/truncate|line-clamp|overflow-hidden/);
    expect([...document.querySelectorAll("style")].some((s) => s.textContent?.includes("size: 58mm auto"))).toBe(true);
  });
});
