"use client";

import { useParams } from "next/navigation";

import { ProductDetail } from "@/components/catalog/product-detail";
import { EditProduct } from "@/components/catalog/product-form";
import { RequirePermission } from "@/components/panel/settings-nav";

export function ProductPage() {
  const { id } = useParams<{ id: string }>();
  return <ProductDetail id={id} />;
}

export function EditProductPage() {
  const { id } = useParams<{ id: string }>();
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Ürünü düzenle</h1>
      <RequirePermission permission="manageCatalog">
        <EditProduct id={id} />
      </RequirePermission>
    </div>
  );
}
