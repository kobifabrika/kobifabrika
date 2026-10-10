import type { Metadata } from "next";
import { Suspense } from "react";

import { ContactPage } from "./contact-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Müşteri kartı" };

export default function ContactDetailPage() {
  return (
    <Suspense>
      <ContactPage basePath="/panel/musteriler" />
    </Suspense>
  );
}
