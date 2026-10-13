import type { Metadata } from "next";
import { Suspense } from "react";

import { RequirePermission } from "@/components/panel/settings-nav";
import { StaffList } from "@/components/staff/staff-list";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Personel" };

export default function StaffPage() {
  return (
    <div className="mx-auto max-w-6xl">
      <RequirePermission permission="viewStaff">
        <Suspense>
          <StaffList />
        </Suspense>
      </RequirePermission>
    </div>
  );
}
