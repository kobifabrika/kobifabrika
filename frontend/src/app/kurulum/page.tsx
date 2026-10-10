import type { Metadata } from "next";

import { Wordmark } from "@/components/brand/wordmark";

import { LogoutLink } from "./logout-link";
import { SetupWizard } from "./setup-wizard";

export const metadata: Metadata = { title: "İşletme kurulumu" };

/** Kurulum sihirbazı: panelden ayrı, tek sütunlu düzen. Sihirbaz bitmeden panele girilmez. */
export default function SetupPage() {
  return (
    <div className="min-h-screen bg-background">
      <header className="flex h-14 items-center justify-between border-b border-border bg-white px-4">
        <Wordmark className="text-xl" />
        <LogoutLink />
      </header>
      <main className="mx-auto w-full max-w-xl px-4 pt-6">
        <SetupWizard />
      </main>
    </div>
  );
}
