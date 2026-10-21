import type { Metadata } from "next";
import { Suspense } from "react";

import { PlatformShell } from "@/components/platform/platform-shell";

/** Oturum istemcide doğrulanır (panel gibi); anında gezinme doğrulamasının dışında. */
export const instant = false;

export const metadata: Metadata = {
  title: { default: "Süper admin", template: "%s | Süper admin | KobiFabrika" },
  robots: { index: false },
};

/** Süper admin paneli (/platform): sadece KobiFabrika ekibi. Canlıda app.kobifabrika.com/platform. */
export default function PlatformLayout({ children }: LayoutProps<"/platform">) {
  return (
    <Suspense>
      <PlatformShell>{children}</PlatformShell>
    </Suspense>
  );
}
