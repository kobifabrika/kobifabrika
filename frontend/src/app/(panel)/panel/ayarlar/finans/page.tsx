import type { Metadata } from "next";

import { RequirePermission } from "@/components/panel/settings-nav";

import { FinanceSettings } from "./finance-settings";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Finans ayarları" };

export default function FinanceSettingsPage() {
  return (
    <RequirePermission permission="viewFinance">
      <FinanceSettings />
    </RequirePermission>
  );
}
