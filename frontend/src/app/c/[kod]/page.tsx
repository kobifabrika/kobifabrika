import type { Metadata } from "next";
import { Suspense } from "react";

import { OptOutView } from "./opt-out-view";

export const metadata: Metadata = {
  title: { absolute: "SMS'leri durdur" },
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

/** Pazarlama SMS'indeki kısa çıkış linki: tek ekranda "SMS'leri durdur". Oturum gerekmez. */
export default function OptOutPage() {
  return (
    <Suspense>
      <OptOutView />
    </Suspense>
  );
}
