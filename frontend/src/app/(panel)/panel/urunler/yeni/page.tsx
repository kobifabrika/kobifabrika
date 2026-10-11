import type { Metadata } from "next";
import Link from "next/link";

import { ProductForm } from "@/components/catalog/product-form";
import { RequirePermission } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Yeni ürün/hizmet" };

export default function NewProductPage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/panel/urunler" className="w-fit text-sm font-semibold text-neutral-700 hover:text-black">
          ← Ürünler/Hizmetler
        </Link>
        <h1 className="text-2xl font-bold tracking-tight text-black">Yeni ürün/hizmet</h1>
      </div>
      <RequirePermission permission="manageCatalog">
        <ProductForm />
      </RequirePermission>
    </div>
  );
}
