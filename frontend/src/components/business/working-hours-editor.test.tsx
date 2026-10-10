import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";

import type { WorkingHour } from "@/lib/api/types";

import { defaultWeek, invalidDays, WorkingHoursEditor } from "./working-hours-editor";

function Harness({ onState }: { onState?: (v: WorkingHour[]) => void }) {
  const [value, setValue] = useState(defaultWeek());
  onState?.(value);
  return <WorkingHoursEditor value={value} onChange={setValue} />;
}

describe("WorkingHoursEditor", () => {
  it("varsayılan: Pzt-Cmt 09:00-19:00 açık, Pazar kapalı", () => {
    render(<Harness />);

    expect(screen.getByLabelText("Pazartesi açılış")).toHaveValue("09:00");
    expect(screen.getByLabelText("Cumartesi kapanış")).toHaveValue("19:00");
    expect(screen.getByRole("switch", { name: "Pazar açık" })).toHaveAttribute("aria-checked", "false");
    expect(screen.queryByLabelText("Pazar açılış")).not.toBeInTheDocument();
  });

  it("ilk açık günün saatlerini tüm açık günlere uygular, kapalı günlere dokunmaz", () => {
    let state: WorkingHour[] = [];
    render(<Harness onState={(v) => (state = v)} />);

    fireEvent.change(screen.getByLabelText("Pazartesi açılış"), { target: { value: "08:30" } });
    fireEvent.change(screen.getByLabelText("Pazartesi kapanış"), { target: { value: "17:00" } });
    fireEvent.click(screen.getByRole("button", { name: /tüm açık günlere uygula/ }));

    expect(screen.getByLabelText("Cuma açılış")).toHaveValue("08:30");
    expect(screen.getByLabelText("Cumartesi kapanış")).toHaveValue("17:00");
    expect(state.find((d) => d.day === "Sunday")).toMatchObject({ isClosed: true, opensAt: "09:00" });
  });

  it("bir günü açıp kapatabilir", () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole("switch", { name: "Pazar açık" }));

    expect(screen.getByLabelText("Pazar açılış")).toHaveValue("09:00");
  });

  it("kapanışı açılıştan önce olan açık günleri bulur", () => {
    const days = defaultWeek().map((d) => (d.day === "Monday" ? { ...d, opensAt: "19:00", closesAt: "09:00" } : d));
    expect(invalidDays(days)).toEqual(["Pazartesi"]);
  });
});
