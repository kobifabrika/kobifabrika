import type { Metadata } from "next";

import { HolidaysCard } from "@/components/appointments/holidays-card";

import { WorkingHoursSettings } from "./working-hours-settings";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Çalışma saatleri" };

export default function WorkingHoursPage() {
  return (
    <div className="flex flex-col gap-6">
      <WorkingHoursSettings />
      <HolidaysCard />
    </div>
  );
}
