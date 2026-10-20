import type { Metadata } from "next";
import { Suspense } from "react";

import { CustomerNotice } from "@/components/booking/customer-notice";

export const metadata: Metadata = {
  title: { absolute: "Aydınlatma metni · Online randevu" },
  robots: { index: false, follow: false },
};

/** İşletmenin müşterileri için KVKK aydınlatma metni (şablon, işletme bilgileriyle doldurulur). Online randevu formundan bağlanır. */
export default function CustomerNoticePage() {
  return (
    <Suspense>
      <CustomerNotice />
    </Suspense>
  );
}
