import type { Metadata } from "next";
import { Suspense } from "react";

import { PaymentView } from "./payment-view";

export const metadata: Metadata = {
  title: { absolute: "Ödeme bilgileri" },
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

/** Tahsilat linki: tutar, IBAN ve "Ödedim". Oturum gerekmez. */
export default function PaymentPage() {
  return (
    <Suspense>
      <PaymentView />
    </Suspense>
  );
}
