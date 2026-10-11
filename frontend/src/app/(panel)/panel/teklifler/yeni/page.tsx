import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";

import { RequirePermission } from "@/components/panel/settings-nav";
import { QuoteForm } from "@/components/quotes/quote-form";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Yeni teklif" };

export default function NewQuotePage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/panel/satislar?sekme=teklifler" className="w-fit text-sm font-semibold text-neutral-700 hover:text-black">
          ← Teklifler
        </Link>
        <h1 className="text-2xl font-bold tracking-tight text-black">Yeni teklif</h1>
      </div>
      <RequirePermission permission="createSales">
        <Suspense>
          <QuoteForm />
        </Suspense>
      </RequirePermission>
    </div>
  );
}
