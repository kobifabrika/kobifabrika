import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { renderWithSession } from "@/test/session";

import { TodayGreeting } from "./today-greeting";

describe("TodayGreeting", () => {
  it("selamlamanın altında işletme adını gösterir", () => {
    renderWithSession(<TodayGreeting />, { businessName: "Moda Butik" });

    const heading = screen.getByRole("heading", { level: 1 });
    expect(heading).toHaveTextContent(/, Emir$/);
    const name = screen.getByText("Moda Butik");
    expect(heading.compareDocumentPosition(name) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(name).toHaveAttribute("title", "Moda Butik");
  });
});
