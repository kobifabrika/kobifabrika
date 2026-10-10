import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { renderWithSession } from "@/test/session";

import { ImportWizard } from "./import-wizard";

const ok = (data: unknown) => ({ data, response: new Response(null, { status: 200 }) });

vi.mock("@/lib/api/client", async (original) => ({
  ...(await original<typeof import("@/lib/api/client")>()),
  api: {
    POST: () =>
      Promise.resolve(
        ok({
          total: 3,
          ok: 1,
          errors: 1,
          duplicates: 1,
          rows: [
            {
              rowNumber: 2,
              name: "Hatalı Telefon",
              phone: null,
              status: "Error",
              messages: ["Telefon geçersiz: 12345"],
            },
            {
              rowNumber: 3,
              name: "Tekrar",
              phone: "+905320000001",
              status: "Duplicate",
              messages: ["Bu numara Kayıtlı Müşteri adına kayıtlı; atlanacak."],
            },
            { rowNumber: 4, name: "Sağlam", phone: null, status: "Ok", messages: [] },
          ],
        }),
      ),
  },
}));

describe("ImportWizard", () => {
  it("yükle → eşle → önizlemede hatalı satır nedeniyle işaretlenir", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            jobId: "j1",
            fileName: "musteriler.xlsx",
            headers: ["Ad", "Telefon"],
            rowCount: 3,
            mapping: { firstName: 0, phone: 1 },
            fields: [
              { key: "firstName", label: "Ad" },
              { key: "phone", label: "Telefon" },
            ],
          }),
          { status: 200 },
        ),
      ),
    );
    renderWithSession(<ImportWizard />);

    const file = new File(["x"], "musteriler.xlsx");
    fireEvent.change(screen.getByLabelText(/Excel dosyası seçin/), { target: { files: [file] } });

    expect(await screen.findByText("musteriler.xlsx")).toBeInTheDocument();
    expect(screen.getByLabelText("Ad")).toHaveValue("0");
    fireEvent.click(screen.getByRole("button", { name: "Önizle" }));

    // Hatalı satır varsa önizleme "Hatalı" süzgeciyle açılır.
    expect(await screen.findByText("Telefon geçersiz: 12345")).toBeInTheDocument();
    expect(screen.queryByText("Sağlam")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Tekrar (1)" }));
    expect(screen.getByText("Bu numara Kayıtlı Müşteri adına kayıtlı; atlanacak.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "1 kaydı aktar" })).toBeEnabled();
    vi.unstubAllGlobals();
  });
});
