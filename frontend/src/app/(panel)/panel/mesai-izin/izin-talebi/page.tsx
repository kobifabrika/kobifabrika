import type { Metadata } from "next";

import { LeaveRequestForm } from "@/components/attendance/leave-request-form";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "İzin talebi" };

export default function LeaveRequestPage() {
  return (
    <div className="mx-auto max-w-6xl">
      <LeaveRequestForm />
    </div>
  );
}
