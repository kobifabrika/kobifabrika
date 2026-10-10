import type { Metadata } from "next";
import { Suspense } from "react";

import { ContactPage } from "../../../musteriler/[id]/contact-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Tedarikçi kartı" };

export default function SupplierDetailPage() {
  return (
    <Suspense>
      <ContactPage basePath="/panel/cari-kasa/tedarikciler" />
    </Suspense>
  );
}
