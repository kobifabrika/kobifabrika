import type { Metadata } from "next";
import Link from "next/link";

import { ImportWizard } from "@/components/contacts/import-wizard";
import { RequirePermission } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Excel'den ürün aktar" };

export default function ProductImportPage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/panel/urunler" className="w-fit text-sm font-semibold text-neutral-700 hover:text-black">
          ← Ürünler/Hizmetler
        </Link>
        <h1 className="text-2xl font-bold tracking-tight text-black">Excel&apos;den ürün aktar</h1>
      </div>
      <RequirePermission permission="manageCatalog">
        <ImportWizard kind="products" />
      </RequirePermission>
    </div>
  );
}
