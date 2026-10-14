"use client";

import { useSearchParams } from "next/navigation";

import { TabNav } from "@/components/contacts/contact-bits";

import { PackageList } from "./packages";
import { ProductList } from "./product-list";

/** Ürünler sayfası sekmeleri: ?sekme=paketler. */
export function CatalogHome() {
  const params = useSearchParams();
  const active = params.get("sekme") === "paketler" ? "paketler" : "urunler";
  return (
    <div className="flex flex-col gap-5">
      <TabNav
        label="Katalog"
        active={active}
        tabs={[
          { key: "urunler", label: "Ürünler/Hizmetler", href: "/panel/urunler" },
          { key: "paketler", label: "Paketler", href: "/panel/urunler?sekme=paketler" },
        ]}
      />
      {active === "paketler" ? <PackageList /> : <ProductList />}
    </div>
  );
}
