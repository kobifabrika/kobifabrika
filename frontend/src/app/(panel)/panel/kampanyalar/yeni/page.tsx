import type { Metadata } from "next";
import { Suspense } from "react";

import { CampaignWizard } from "@/components/marketing/campaign-wizard";
import { RequirePermission } from "@/components/panel/settings-nav";

export const instant = false;

export const metadata: Metadata = { title: "Yeni kampanya" };

export default function NewCampaignPage() {
  return (
    <RequirePermission permission="manageMarketing">
      <Suspense>
        <CampaignWizard />
      </Suspense>
    </RequirePermission>
  );
}
