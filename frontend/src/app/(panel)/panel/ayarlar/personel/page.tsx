import type { Metadata } from "next";

import { RequirePermission } from "@/components/panel/settings-nav";

import { StaffSettings } from "./staff-settings";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Personel ayarları" };

export default function StaffSettingsPage() {
  return (
    <RequirePermission permission="viewSettings">
      <StaffSettings />
    </RequirePermission>
  );
}
