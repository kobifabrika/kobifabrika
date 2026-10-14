import type { Metadata } from "next";
import { Suspense } from "react";

import { CatalogHome } from "@/components/catalog/catalog-home";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Ürünler/Hizmetler" };

export default function ProductsPage() {
  return (
    <div className="mx-auto max-w-6xl">
      <Suspense>
        <CatalogHome />
      </Suspense>
    </div>
  );
}
