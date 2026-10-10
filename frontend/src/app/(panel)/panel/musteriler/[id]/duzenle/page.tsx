import type { Metadata } from "next";
import { Suspense } from "react";

import { EditContactPage } from "../contact-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Kartı düzenle" };

export default function EditPage() {
  return (
    <Suspense>
      <EditContactPage />
    </Suspense>
  );
}
