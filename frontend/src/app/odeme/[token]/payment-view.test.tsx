import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { PaymentView } from "./payment-view";

vi.mock("next/navigation", () => ({ useParams: () => ({ token: "tok" }) }));

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });
const payment = {
  businessName: "Şık Kuaför",
  logoUrl: null,
  businessPhone: "0212 555 44 33",
  contactName: "Ayşe Yılmaz",
  amount: 1250.5,
  note: "Ekim ayı",
  iban: "TR400006200000000000000001",
  ibanHolderName: "Şık Kuaför Ltd.",
  status: "Open",
  expiresAt: "2026-11-10T00:00:00Z",
  claimedAt: null,
};
const claim = vi.fn();

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    GET: () => Promise.resolve(ok(payment)),
    POST: (_: string, init: { body: { note: string | null } }) => {
      claim(init.body);
      return Promise.resolve(ok({ ...payment, status: "ClaimedPaid", claimedAt: new Date().toISOString() }));
    },
  },
}));

function renderView() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <PaymentView />
    </QueryClientProvider>,
  );
}

describe("/odeme/[token]", () => {
  it("işletme adı üstte, tutar ve IBAN görünür; IBAN kopyalanır", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });
    renderView();

    expect(await screen.findByText("Şık Kuaför")).toBeInTheDocument();
    expect(screen.getByText("1.250,50 TL")).toBeInTheDocument();
    expect(screen.getByText("TR40 0006 2000 0000 0000 0000 01")).toBeInTheDocument();
    expect(screen.getByText("KobiFabrika ile gönderildi")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "IBAN: kopyala" }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith("TR400006200000000000000001"));
  });

  it("Ödedim'e basınca not gönderilir ve teşekkür mesajı çıkar", async () => {
    renderView();

    fireEvent.change(await screen.findByLabelText("Not (isteğe bağlı)"), { target: { value: "Garanti'den" } });
    fireEvent.click(screen.getByRole("button", { name: "Ödedim" }));

    expect(await screen.findByText(/bildiriminiz iletildi/)).toBeInTheDocument();
    expect(claim).toHaveBeenCalledWith({ note: "Garanti'den" });
    expect(screen.queryByRole("button", { name: "Ödedim" })).not.toBeInTheDocument();
  });
});
