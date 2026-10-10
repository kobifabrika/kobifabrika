import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { useForm } from "react-hook-form";
import { describe, expect, it, vi } from "vitest";

import { BusinessFields, type BusinessBasics } from "./business-fields";
import { sectors } from "./sectors";

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: { GET: () => new Promise(() => {}) },
}));

function Harness() {
  const form = useForm<BusinessBasics>({ defaultValues: { name: "", phone: "", city: "", district: "" } });
  return (
    <BusinessFields control={form.control} register={form.register} errors={{}} onCityChange={() => {}} />
  );
}

function renderFields() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <Harness />
    </QueryClientProvider>,
  );
}

describe("BusinessFields sektör kartları", () => {
  it("tüm sektörleri eşit yükseklikte satırlara dizer", () => {
    renderFields();

    const group = screen.getByRole("radiogroup", { name: "Sektör" });
    expect(group).toHaveClass("auto-rows-fr");
    expect(screen.getAllByRole("radio")).toHaveLength(sectors.length);
  });

  it("boşluksuz uzun adlar kartın içinde alt satıra inebilir ('/' sonrası <wbr>)", () => {
    renderFields();

    const card = screen.getByRole("radio", { name: "Perakende/Mağaza" });
    expect(card).toHaveClass("min-w-0");
    const label = card.querySelector("span")!;
    expect(label.innerHTML).toBe("Perakende/<wbr>Mağaza");
    expect(label).toHaveClass("[overflow-wrap:anywhere]");
  });

  it("karta dokununca seçilir", () => {
    renderFields();

    const card = screen.getByRole("radio", { name: "Perakende/Mağaza" });
    fireEvent.click(card);
    expect(card).toHaveAttribute("aria-checked", "true");
  });
});
