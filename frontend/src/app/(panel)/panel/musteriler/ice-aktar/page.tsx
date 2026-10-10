import type { Metadata } from "next";
import Link from "next/link";

import { ImportWizard } from "@/components/contacts/import-wizard";
import { RequirePermission } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Excel'den müşteri aktar" };

export default function ImportPage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/panel/musteriler" className="w-fit text-sm font-semibold text-neutral-700 hover:text-black">
          ← Müşteriler
        </Link>
        <h1 className="text-2xl font-bold tracking-tight text-black">Excel&apos;den müşteri aktar</h1>
      </div>
      <RequirePermission permission="writeContacts">
        <ImportWizard />
      </RequirePermission>
    </div>
  );
}
