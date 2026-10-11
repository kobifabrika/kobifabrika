import type { Metadata } from "next";
import { Suspense } from "react";

import { SalePage } from "./sale-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Satış" };

export default function SaleDetailPage() {
  return (
    <Suspense>
      <SalePage />
    </Suspense>
  );
}
