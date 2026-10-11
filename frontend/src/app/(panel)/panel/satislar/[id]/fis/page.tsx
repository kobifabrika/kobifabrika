import type { Metadata } from "next";
import { Suspense } from "react";

import { ReceiptPage } from "../sale-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Fiş" };

export default function SaleReceiptPage() {
  return (
    <Suspense>
      <ReceiptPage />
    </Suspense>
  );
}
