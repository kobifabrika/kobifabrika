import type { Metadata } from "next";
import { Suspense } from "react";

import { QuotePage } from "./quote-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Teklif" };

export default function QuoteDetailPage() {
  return (
    <Suspense>
      <QuotePage />
    </Suspense>
  );
}
