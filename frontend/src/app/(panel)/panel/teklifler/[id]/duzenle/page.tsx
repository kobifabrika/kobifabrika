import type { Metadata } from "next";
import { Suspense } from "react";

import { EditQuotePage } from "../quote-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Teklifi düzenle" };

export default function EditPage() {
  return (
    <Suspense>
      <EditQuotePage />
    </Suspense>
  );
}
