import type { Metadata } from "next";
import { Suspense } from "react";

import { RequirePermission } from "@/components/panel/settings-nav";
import { QuickSale } from "@/components/sales/quick-sale";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Hızlı satış" };

export default function NewSalePage() {
  return (
    <div className="mx-auto max-w-6xl">
      <RequirePermission permission="createSales">
        <Suspense>
          <QuickSale />
        </Suspense>
      </RequirePermission>
    </div>
  );
}
