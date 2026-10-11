import type { Metadata } from "next";
import { Suspense } from "react";

import { StockOverview } from "@/components/catalog/stock-overview";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Stok" };

export default function StockPage() {
  return (
    <div className="mx-auto max-w-6xl">
      <Suspense>
        <StockOverview />
      </Suspense>
    </div>
  );
}
