import type { Metadata } from "next";

import { SmsSettings } from "@/components/marketing/sms-credit";
import { RequirePermission } from "@/components/panel/settings-nav";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "SMS ayarları" };

export default function SmsSettingsPage() {
  return (
    <RequirePermission permission="viewMarketing">
      <SmsSettings />
    </RequirePermission>
  );
}
