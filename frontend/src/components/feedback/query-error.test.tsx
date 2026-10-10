import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { NetworkError } from "@/lib/api/client";

import { QueryError } from "./query-error";

describe("QueryError", () => {
  it("Türkçe hata ve 'Tekrar dene' gösterir; tıklayınca yeniden dener", async () => {
    const onRetry = vi.fn().mockResolvedValue(undefined);
    render(<QueryError error={new NetworkError(false)} onRetry={onRetry} />);

    expect(screen.getByRole("alert")).toHaveTextContent("Sunucuya ulaşılamıyor.");
    fireEvent.click(screen.getByRole("button", { name: "Tekrar dene" }));

    await waitFor(() => expect(onRetry).toHaveBeenCalledOnce());
  });

  it("verilen mesajı hata metninin yerine gösterir", () => {
    render(<QueryError error={new Error("x")} message="Kayıt bulunamadı." onRetry={() => undefined} />);

    expect(screen.getByRole("alert")).toHaveTextContent("Kayıt bulunamadı.");
  });
});
