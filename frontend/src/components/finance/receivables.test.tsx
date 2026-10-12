import { fireEvent, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { ReminderSheet } from "./receivables";

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });
const post = vi.fn();

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    POST: (path: string) => {
      post(path);
      return Promise.resolve(
        ok({
          contactId: "c1",
          contactName: "Ayşe Yılmaz",
          phone: "+905321234567",
          amount: 1250.5,
          dueDate: "2026-10-10",
          text: "Merhaba Ayşe Yılmaz, Kaya Kırtasiye olarak hatırlatmak isteriz: 10.10.2026 vadeli 1.250,50 TL ödemeniz bulunuyor.",
          whatsappUrl: "https://wa.me/905321234567?text=Merhaba",
          smsUrl: "sms:+905321234567?&body=Merhaba",
          paymentLinkUrl: null,
          paymentLinkCreated: false,
        }),
      );
    },
  },
}));

afterEach(() => vi.unstubAllGlobals());

describe("Hatırlatma penceresi", () => {
  it("açılırken mesajı POST ile hazırlar; WhatsApp ve SMS tıklanmadan önce gerçek linklerdir", async () => {
    const fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 204 })));
    vi.stubGlobal("fetch", fetchMock);
    renderWithSession(<ReminderSheet row={{ contactId: "c1", name: "Ayşe Yılmaz" }} onClose={() => undefined} />);

    const whatsapp = await screen.findByRole("link", { name: /WhatsApp/ });
    expect(post).toHaveBeenCalledWith("/api/receivables/{contactId}/reminder-message");
    expect(whatsapp).toHaveAttribute("href", "https://wa.me/905321234567?text=Merhaba");
    expect(whatsapp).toHaveAttribute("target", "_blank");
    expect(screen.getByRole("link", { name: /SMS/ })).toHaveAttribute("href", "sms:+905321234567?&body=Merhaba");
    expect(screen.getByRole("button", { name: /Metni kopyala/ })).toBeInTheDocument();
    expect(screen.getByText(/1\.250,50 TL ödemeniz/)).toBeInTheDocument();

    // Tıklama kaydı keepalive ile gider (sayfa uygulamaya geçse de istek tamamlanır).
    fireEvent.click(screen.getByRole("link", { name: /SMS/ }));
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/api/receivables/c1/reminders"),
      expect.objectContaining({ method: "POST", keepalive: true, credentials: "include", body: JSON.stringify({ channel: "Sms" }) }),
    );
  });
});
