import type { Metadata } from "next";
import { Suspense } from "react";

import { InvitationView } from "./invitation-view";

export const metadata: Metadata = { title: "Ekip daveti" };

export default function InvitationPage() {
  return (
    <>
      <h1 className="text-3xl font-bold tracking-tight text-foreground">Ekip daveti</h1>
      <Suspense>
        <InvitationView />
      </Suspense>
    </>
  );
}
