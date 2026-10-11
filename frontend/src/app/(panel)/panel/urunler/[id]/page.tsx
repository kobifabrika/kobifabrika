import type { Metadata } from "next";
import { Suspense } from "react";

import { ProductPage } from "./product-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Ürün kartı" };

export default function ProductDetailPage() {
  return (
    <Suspense>
      <ProductPage />
    </Suspense>
  );
}
