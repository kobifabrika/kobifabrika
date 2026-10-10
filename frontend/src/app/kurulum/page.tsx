import type { Metadata } from "next";

import { Backdrop } from "@/components/brand/backdrop";
import { Wordmark } from "@/components/brand/wordmark";

import { LogoutLink } from "./logout-link";
import { SetupWizard } from "./setup-wizard";

export const metadata: Metadata = { title: "İşletme kurulumu" };

/** Kurulum sihirbazı: panelden ayrı, tek sütunlu, koyu tema (giriş ekranlarıyla aynı dil). Sihirbaz bitmeden panele girilmez. */
export default function SetupPage() {
  return (
    <div className="dark relative min-h-screen overflow-x-clip bg-background text-foreground">
      <Backdrop grid={false} />
      <header className="relative flex h-14 items-center justify-between border-b border-border px-4 sm:px-6">
        <Wordmark onDark className="text-xl" />
        <LogoutLink />
      </header>
      <main className="relative mx-auto w-full max-w-xl px-4 pt-6">
        <SetupWizard />
      </main>
    </div>
  );
}
