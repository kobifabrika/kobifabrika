import type { Metadata } from "next";
import { Suspense } from "react";

import { MarketingHome } from "@/components/marketing/marketing-home";
import { RequirePermission } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Kampanyalar" };

export default function CampaignsPage() {
  return (
    <RequirePermission permission="viewMarketing">
      <Suspense>
        <MarketingHome />
      </Suspense>
    </RequirePermission>
  );
}
