import type { Metadata } from "next";

import { PlatformBusinesses } from "@/components/platform/platform-businesses";

export const instant = false;

export const metadata: Metadata = { title: "İşletmeler" };

export default function PlatformBusinessesPage() {
  return <PlatformBusinesses />;
}
