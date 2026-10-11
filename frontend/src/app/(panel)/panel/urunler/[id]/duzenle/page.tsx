import type { Metadata } from "next";
import { Suspense } from "react";

import { EditProductPage } from "../product-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Ürünü düzenle" };

export default function EditPage() {
  return (
    <Suspense>
      <EditProductPage />
    </Suspense>
  );
}
