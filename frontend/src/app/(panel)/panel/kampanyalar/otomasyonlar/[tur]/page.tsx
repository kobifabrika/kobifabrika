import type { Metadata } from "next";
import { Suspense } from "react";

import { AutomationEditor } from "@/components/marketing/automations";
import { RequirePermission } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Otomasyon" };

export default function AutomationPage() {
  return (
    <RequirePermission permission="viewMarketing">
      <Suspense>
        <AutomationEditor />
      </Suspense>
    </RequirePermission>
  );
}
