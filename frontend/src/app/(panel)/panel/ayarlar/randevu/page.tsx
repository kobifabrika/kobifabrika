import type { Metadata } from "next";

import { AppointmentSettings } from "@/components/appointments/appointment-settings";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Randevu ayarları" };

export default function AppointmentSettingsPage() {
  return <AppointmentSettings />;
}
