import type { Metadata } from "next";
import { Suspense } from "react";

import { RequirePermission } from "@/components/panel/settings-nav";

import { StaffPage } from "./staff-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Personel kartı" };

export default function StaffDetailPage() {
  return (
    <div className="mx-auto max-w-6xl">
      <RequirePermission permission="viewStaff">
        <Suspense>
          <StaffPage />
        </Suspense>
      </RequirePermission>
    </div>
  );
}
