import type { Metadata } from "next";
import { Suspense } from "react";

import { CampaignDetail } from "@/components/marketing/campaigns";
import { RequirePermission } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Kampanya" };

export default function CampaignPage() {
  return (
    <RequirePermission permission="viewMarketing">
      <Suspense>
        <CampaignDetail />
      </Suspense>
    </RequirePermission>
  );
}
