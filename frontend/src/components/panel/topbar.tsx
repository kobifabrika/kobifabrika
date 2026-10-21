"use client";

import { BusinessSwitcher } from "@/components/panel/business-switcher";
import { CommandMenu } from "@/components/panel/command-menu";
import { HelpMenu } from "@/components/panel/help-menu";
import { MobileMenu } from "@/components/panel/mobile-menu";
import { NotificationBell } from "@/components/panel/notification-bell";
import { QuickActionsMenu } from "@/components/panel/quick-actions-menu";
import { UserMenu } from "@/components/panel/user-menu";

/** Panel üst çubuğu (siyah, sol menüyle aynı dil; masaüstünde 52 px, düğmeler 36 px; 2026-10-21): telefonda işletme adı, arama, zil ve sağda Menü (kullanıcı menüsüyle birleşik); masaüstünde arama (Ctrl+K), (+) hızlı işlem, Yardım (?), zil, kullanıcı menüsü. */
export function Topbar() {
  return (
    <header className="dark sticky top-0 z-20 flex h-12 items-center justify-between gap-2 border-b border-sidebar-border bg-background px-4 text-foreground lg:h-[52px] lg:px-8 print:hidden">
      {/* Telefonda wordmark yerine işletme adı (wordmark Menü panelinde); masaüstünde ad sol menüde. */}
      <BusinessSwitcher variant="topbar" className="lg:hidden" />
      <div className="flex shrink-0 items-center justify-end gap-1 lg:flex-1 lg:justify-between">
        <CommandMenu />
        <div className="flex items-center gap-1">
          <QuickActionsMenu />
          <HelpMenu />
          <NotificationBell />
          <UserMenu />
          <MobileMenu />
        </div>
      </div>
    </header>
  );
}
