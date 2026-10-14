import type { Metadata } from "next";

import { PlatformCredit } from "@/components/marketing/platform-credit";

export const instant = false;

export const metadata: Metadata = { title: "Kredi yükle (platform)" };

/** Sadece KobiFabrika ekibi (PlatformAdmin). Tam süper admin paneli Sprint 8'de. */
export default function PlatformCreditPage() {
  return <PlatformCredit />;
}
