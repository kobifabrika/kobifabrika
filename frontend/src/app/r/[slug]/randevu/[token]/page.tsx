import type { Metadata } from "next";
import { Suspense } from "react";

import { ManageRoute } from "@/components/booking/booking-route";

export const metadata: Metadata = {
  title: { absolute: "Randevunuz" },
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

/** Müşterinin randevu sayfası: özet ve (süre sınırı geçmediyse) iptal. SMS'teki ve onay ekranındaki link. */
export default function ManageAppointmentPage() {
  return (
    <Suspense>
      <ManageRoute />
    </Suspense>
  );
}
