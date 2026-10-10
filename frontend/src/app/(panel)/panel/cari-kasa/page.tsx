import type { Metadata } from "next";
import { Suspense } from "react";

import { FinanceHome } from "./finance-home";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Cari ve Kasa" };

export default function FinancePage() {
  return (
    <Suspense>
      <FinanceHome />
    </Suspense>
  );
}
