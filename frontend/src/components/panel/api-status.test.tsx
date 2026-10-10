import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ApiStatus } from "./api-status";

vi.mock("@tanstack/react-query", () => ({
  useQuery: () => ({ data: { version: "0.1.0" }, isPending: false, isError: false }),
}));

describe("ApiStatus", () => {
  afterEach(() => vi.unstubAllEnvs());

  it("geliştirme ortamında API durumunu gösterir", () => {
    vi.stubEnv("NODE_ENV", "development");
    render(<ApiStatus />);

    expect(screen.getByRole("status")).toHaveTextContent("API bağlı · v0.1.0");
  });

  it("production'da hiçbir şey render etmez", () => {
    vi.stubEnv("NODE_ENV", "production");
    const { container } = render(<ApiStatus />);

    expect(container).toBeEmptyDOMElement();
  });
});
