"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "cn";

import { Logo } from "@/components/brand/logo";
import { BusinessSwitcher } from "@/components/panel/business-switcher";
import { ApiStatus } from "@/components/panel/api-status";
import { useSession } from "@/components/providers/session-provider";
import { api, unwrap } from "@/lib/api/client";
import { isActive, panelHref, visibleNavGroups } from "@/lib/navigation";

/** Panel menü listesi. Masaüstü sol menüde ve telefondaki "Menü" panelinde kullanılır. */
export function NavList({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  const { role, business } = useSession();
  // Stok menüsünde kritik ve eksi stoktaki ürün sayısı (sayı rozeti).
  const stock = useQuery({
    queryKey: ["stock", "summary"],
    queryFn: () => unwrap(api.GET("/api/stock/summary")),
    enabled: business.modules.stock,
    staleTime: 60_000,
  });
  const lowStock = (stock.data?.low ?? 0) + (stock.data?.negative ?? 0);

  return (
    <nav aria-label="Ana menü" className="flex flex-col gap-6">
      {visibleNavGroups(role, business.modules).map((group) => (
        <div key={group.title}>
          <p className="mb-2 px-3 text-xs font-semibold tracking-wider text-neutral-400 uppercase">{group.title}</p>
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
                      "flex min-h-11 items-center gap-3 rounded-md px-3 text-sm outline-sidebar-ring transition-colors",
                      active
                        ? "bg-sidebar-primary font-semibold text-sidebar-primary-foreground"
                        : "text-sidebar-foreground hover:bg-sidebar-accent hover:text-white",
                    )}
                  >
                    <Icon
                      aria-hidden
                      className={cn(
                        "size-5 shrink-0",
                        active ? "text-sidebar-primary-foreground" : "text-sidebar-foreground",
                      )}
                    />
                    <span className="flex-1">{label}</span>
                    {slug === "stok" && lowStock > 0 && (
                      <span
                        className="rounded-full bg-white px-2 text-xs font-semibold text-black tabular-nums"
                        aria-label={`${lowStock} ürün kritik seviyede`}
                      >
                        {lowStock}
                      </span>
                    )}
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

/**
 * Menünün alt satırı: küçük KobiFabrika logosu (koyu zemin, 14 px yükseklik), geliştirmede altında API durumu,
 * ikisi aynı sol kenarda. Masaüstü sol menüde ve telefondaki "Menü" panelinde aynı (2026-10-21: menünün üstünde işletme,
 * KobiFabrika altta).
 */
export function NavFooter() {
  return (
    <div className="flex flex-col items-start gap-2 border-t border-sidebar-border px-6 py-4">
      <Logo surface="dark" className="h-3.5" />
      <ApiStatus className="text-sidebar-foreground" />
    </div>
  );
}

/** Masaüstünde (lg ve üstü) sabit koyu sol menü: üstte işletmenin logosu ve adı; aktif öğe mavi zemin + siyah yazı. */
export function Sidebar() {
  return (
    <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 flex-col overflow-hidden border-r border-sidebar-border bg-sidebar text-sidebar-foreground lg:flex">
      {/* Üst çubukla tek bant: aynı zemin, aynı yükseklik (52 px), alt çizgi aynı hizada ve aynı soluk renkte. */}
      <div className="flex h-[52px] shrink-0 items-center border-b border-sidebar-border px-3">
        <BusinessSwitcher variant="sidebar" />
      </div>
      <div className="relative flex-1 overflow-y-auto px-3 py-6">
        <NavList />
      </div>
      <NavFooter />
    </aside>
  );
}
