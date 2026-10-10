import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { ContactForm } from "./contact-form";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), back: vi.fn() }) }));

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    GET: (path: string) => {
      if (path === "/api/contacts/phone-check") {
        return Promise.resolve(
          ok({
            valid: true,
            normalized: "+905321234567",
            existingContactId: "c9",
            existingContactName: "Ayşe Yılmaz",
            existingIsArchived: false,
          }),
        );
      }
      return Promise.resolve(ok([]));
    },
  },
}));

describe("ContactForm", () => {
  it("kayıtlı numara yazılınca 'Bu numara … adına kayıtlı, Karta git' uyarısı çıkar", async () => {
    renderWithSession(<ContactForm />);

    fireEvent.change(screen.getByLabelText("Telefon"), { target: { value: "0532 123 45 67" } });

    expect(await screen.findByText(/Bu numara/)).toHaveTextContent("Bu numara Ayşe Yılmaz adına kayıtlı.");
    expect(screen.getByRole("link", { name: "Karta git" })).toHaveAttribute("href", "/panel/musteriler/c9");
  });

  it("ad ve firma boşsa kaydetmez; KVKK izinleri varsayılan kapalı", async () => {
    renderWithSession(<ContactForm />);

    for (const sw of screen.getAllByRole("switch")) expect(sw).toHaveAttribute("aria-checked", "false");
    fireEvent.click(screen.getByRole("button", { name: "Kaydet" }));

    expect(await screen.findByText("Ad veya firma adı girin.")).toBeInTheDocument();
  });

  it("açılış bakiyesini sadece Sahip/Yönetici görür ve para kuralı uygulanır", async () => {
    const { unmount } = renderWithSession(<ContactForm />, { role: "Staff" });
    expect(screen.queryByText("Açılış bakiyesi (isteğe bağlı)")).not.toBeInTheDocument();
    unmount();

    renderWithSession(<ContactForm />);
    fireEvent.change(screen.getByLabelText("Ad"), { target: { value: "Ayşe" } });
    fireEvent.change(screen.getByLabelText("Tutar (TL)"), { target: { value: "10,001" } });
    fireEvent.click(screen.getByRole("button", { name: "Kaydet" }));

    expect(await screen.findByText("Tutar en fazla 2 ondalık basamak içerebilir.")).toBeInTheDocument();
  });
});
