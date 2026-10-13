import type { Metadata } from "next";
import { Suspense } from "react";

import { CalendarView } from "@/components/appointments/calendar-view";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Randevular" };

export default function AppointmentsPage() {
  return (
    <div className="mx-auto max-w-7xl">
      <Suspense>
        <CalendarView />
      </Suspense>
    </div>
  );
}
