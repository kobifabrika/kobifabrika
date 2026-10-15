import type { Metadata } from "next";
import { Suspense } from "react";

import { AttendanceHome } from "@/components/attendance/attendance-home";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Mesai ve İzin" };

export default function AttendancePage() {
  return (
    <div className="mx-auto max-w-6xl">
      <Suspense>
        <AttendanceHome />
      </Suspense>
    </div>
  );
}
