"use client";

import { BusinessSwitcher } from "@/components/panel/business-switcher";
import { CommandMenu } from "@/components/panel/command-menu";
import { NotificationBell } from "@/components/panel/notification-bell";
import { QuickActionsMenu } from "@/components/panel/quick-actions-menu";
import { UserMenu } from "@/components/panel/user-menu";

/** Panel üst çubuğu (siyah, sol menüyle aynı dil): telefonda işletme adı; arama (Ctrl+K), masaüstünde (+) hızlı işlem, bildirim zili, kullanıcı menüsü. */
export function Topbar() {
  return (
    <header className="dark sticky print:hidden top-0 z-20 flex h-14 items-center justify-between gap-2 border-b border-sidebar-border bg-background px-4 text-foreground lg:h-16 lg:px-8">
      {/* Telefonda wordmark yerine işletme adı (wordmark Menü panelinde); masaüstünde ad sol menüde. */}
      <BusinessSwitcher variant="topbar" className="lg:hidden" />
      <div className="flex shrink-0 items-center justify-end gap-1 lg:flex-1 lg:justify-between">
        <CommandMenu />
        <div className="flex items-center gap-1">
          <QuickActionsMenu />
          <NotificationBell />
          <UserMenu />
        </div>
      </div>
    </header>
  );
}
