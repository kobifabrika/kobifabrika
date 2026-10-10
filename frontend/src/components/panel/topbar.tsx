import Link from "next/link";
import { Bell } from "lucide-react";

import { Wordmark } from "@/components/brand/wordmark";
import { Button } from "@/components/ui/button";

/** Panel üst çubuğu. Telefonda wordmark, masaüstünde sayfa başlığı alanı. */
export function Topbar() {
  return (
    <header className="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-border bg-white px-4 lg:h-16 lg:px-8">
      <Link href="/panel" className="rounded-md lg:hidden" aria-label="KobiFabrika — Bugün">
        <Wordmark className="text-xl" />
      </Link>
      <span className="hidden lg:block" />

      <div className="flex items-center gap-2">
        <Button variant="ghost" size="icon" aria-label="Bildirimler" className="text-brand-900">
          <Bell aria-hidden className="size-5" />
        </Button>
        <div
          aria-label="Kullanıcı"
          className="flex size-11 items-center justify-center rounded-full bg-brand-900 text-sm font-bold text-white"
        >
          KF
        </div>
      </div>
    </header>
  );
}
