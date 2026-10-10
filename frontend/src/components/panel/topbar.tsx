"use client";

import Link from "next/link";

import { Wordmark } from "@/components/brand/wordmark";
import { CommandMenu } from "@/components/panel/command-menu";
import { NotificationBell } from "@/components/panel/notification-bell";
import { UserMenu } from "@/components/panel/user-menu";

/** Panel üst çubuğu (siyah, sol menüyle aynı dil): telefonda wordmark; arama (Ctrl+K), bildirim zili, kullanıcı menüsü. */
export function Topbar() {
  return (
    <header className="dark sticky top-0 z-20 flex h-14 items-center justify-between gap-2 border-b border-sidebar-border bg-background px-4 text-foreground lg:h-16 lg:px-8">
      <Link href="/panel" className="rounded-md lg:hidden" aria-label="KobiFabrika — Bugün">
        <Wordmark onDark className="text-xl" />
      </Link>
      <div className="flex flex-1 items-center justify-end gap-1 lg:justify-between">
        <CommandMenu />
        <div className="flex items-center gap-1">
          <NotificationBell />
          <UserMenu />
        </div>
      </div>
    </header>
  );
}
