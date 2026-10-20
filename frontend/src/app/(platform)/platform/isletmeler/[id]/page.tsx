import type { Metadata } from "next";
import { Suspense } from "react";

import { PlatformBusinessDetail } from "@/components/platform/platform-business-detail";

export const instant = false;

export const metadata: Metadata = { title: "İşletme" };

export default function PlatformBusinessPage() {
  return (
    <Suspense>
      <PlatformBusinessDetail />
    </Suspense>
  );
}
