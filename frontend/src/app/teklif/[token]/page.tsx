import type { Metadata } from "next";
import { Suspense } from "react";

import { QuoteView } from "./quote-view";

export const metadata: Metadata = {
  title: { absolute: "Teklif" },
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

/** Herkese açık teklif: kalemler, toplam, PDF; müşteri onaylar veya reddeder. Oturum gerekmez. */
export default function QuotePage() {
  return (
    <Suspense>
      <QuoteView />
    </Suspense>
  );
}
