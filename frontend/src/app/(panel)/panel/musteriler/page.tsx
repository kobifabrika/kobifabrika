import type { Metadata } from "next";
import { Suspense } from "react";

import { ContactList } from "@/components/contacts/contact-list";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Müşteriler" };

export default function CustomersPage() {
  return (
    <div className="mx-auto max-w-6xl">
      <Suspense>
        <ContactList />
      </Suspense>
    </div>
  );
}
