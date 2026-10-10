import type { Metadata } from "next";
import { Suspense } from "react";

import { NewContact } from "./new-contact";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Yeni müşteri" };

export default function NewContactPage() {
  return (
    <Suspense>
      <NewContact />
    </Suspense>
  );
}
