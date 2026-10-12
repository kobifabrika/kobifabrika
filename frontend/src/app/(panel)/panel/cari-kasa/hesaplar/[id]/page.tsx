import type { Metadata } from "next";
import { Suspense } from "react";

import { AccountPage } from "./account-page";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Hesap kartı" };

export default function AccountDetailPage() {
  return (
    <Suspense>
      <AccountPage />
    </Suspense>
  );
}
