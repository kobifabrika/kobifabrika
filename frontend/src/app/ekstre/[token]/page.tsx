import type { Metadata } from "next";
import { Suspense } from "react";

import { StatementView } from "./statement-view";

export const metadata: Metadata = {
  title: { absolute: "Cari hesap ekstresi" },
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

/** Herkese açık ekstre (30 gün geçerli link). Oturum gerekmez; veri istemcide API'den gelir. */
export default function StatementPage() {
  return (
    <Suspense>
      <StatementView />
    </Suspense>
  );
}
