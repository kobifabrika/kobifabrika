import type { Metadata } from "next";

import { BusinessSettings } from "./business-settings";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Ayarlar" };

export default function SettingsPage() {
  return <BusinessSettings />;
}
