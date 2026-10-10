"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "cn";

import { Wordmark } from "@/components/brand/wordmark";
import { BusinessSwitcher } from "@/components/panel/business-switcher";
import { ApiStatus } from "@/components/panel/api-status";
import { useSession } from "@/components/providers/session-provider";
import { isActive, panelHref, visibleNavGroups } from "@/lib/navigation";

/** Panel menü listesi. Masaüstü sol menüde ve telefondaki "Menü" panelinde kullanılır. */
export function NavList({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  const { role, business } = useSession();

  return (
    <nav aria-label="Ana menü" className="flex flex-col gap-6">
      {visibleNavGroups(role, business.modules).map((group) => (
        <div key={group.title}>
          <p className="mb-2 px-3 text-xs font-semibold tracking-wider text-neutral-400 uppercase">
            {group.title}
          </p>
          <ul className="flex flex-col gap-1">
            {group.items.map(({ slug, label, icon: Icon }) => {
              const active = isActive(pathname, slug);
              return (
                <li key={slug || "bugun"}>
                  <Link
                    href={panelHref(slug)}
                    onClick={onNavigate}
                    aria-current={active ? "page" : undefined}
                    className={cn(
                      "flex min-h-11 items-center gap-3 rounded-md px-3 text-sm transition-colors outline-sidebar-ring",
                      active
                        ? "bg-sidebar-primary font-semibold text-sidebar-primary-foreground"
                        : "text-sidebar-foreground hover:bg-sidebar-accent hover:text-white",
                    )}
                  >
                    <Icon
                      aria-hidden
                      className={cn("size-5 shrink-0", active ? "text-sidebar-primary-foreground" : "text-sidebar-foreground")}
                    />
                    {label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}

/** Masaüstünde (lg ve üstü) sabit siyah sol menü: wordmark, altında işletme adı; aktif öğe mavi zemin + siyah yazı. */
export function Sidebar() {
  return (
    <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 flex-col overflow-hidden border-r border-sidebar-border bg-sidebar text-sidebar-foreground lg:flex">
      <div className="flex flex-col gap-3 border-b border-sidebar-border px-3 pt-4 pb-3">
        <Link href="/panel" className="self-start rounded-md px-3 outline-sidebar-ring" aria-label="KobiFabrika — Bugün">
          <Wordmark onDark className="text-2xl" />
        </Link>
        <BusinessSwitcher variant="sidebar" />
      </div>
      <div className="relative flex-1 overflow-y-auto px-3 py-6">
        <NavList />
      </div>
      <div className="border-t border-sidebar-border px-6 py-4">
        <ApiStatus className="text-sidebar-foreground" />
      </div>
    </aside>
  );
}
