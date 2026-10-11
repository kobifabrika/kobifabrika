import type { Metadata } from "next";
import { Suspense } from "react";

import { SalesHome } from "@/components/sales/sales-home";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Satışlar" };

export default function SalesPage() {
  return (
    <div className="mx-auto max-w-5xl">
      <Suspense>
        <SalesHome />
      </Suspense>
    </div>
  );
}
