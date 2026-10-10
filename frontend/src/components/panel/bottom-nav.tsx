"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { Banknote, CalendarPlus, Menu, Package, Plus, ReceiptText, UserPlus } from "lucide-react";
import { cn } from "cn";

import { Wordmark } from "@/components/brand/wordmark";
import { ApiStatus } from "@/components/panel/api-status";
import { NavList } from "@/components/panel/sidebar";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { useSession } from "@/components/providers/session-provider";
import { bottomTabs, isActive, panelHref } from "@/lib/navigation";

const quickActions = [
  { label: "Yeni satış", href: "/panel/satislar", icon: ReceiptText },
  { label: "Yeni randevu", href: "/panel/randevular", icon: CalendarPlus, module: "appointments" as const },
  { label: "Yeni müşteri", href: "/panel/musteriler", icon: UserPlus },
  { label: "Tahsilat", href: "/panel/cari-kasa", icon: Banknote },
];

const tabClass =
  "flex min-h-14 flex-1 flex-col items-center justify-center gap-0.5 text-xs font-medium transition-colors";

/** Telefonda (lg altı) alt sekme çubuğu: Bugün, Randevu, (+), Satış, Müşteri, Menü. */
export function BottomNav() {
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const [quickOpen, setQuickOpen] = useState(false);
  const { business } = useSession();

  // Randevular modülü kapalıysa (Perakende şablonu) o sekmenin yerinde Ürünler görünür.
  const tabs = bottomTabs.map((t) =>
    t.module && !business.modules[t.module] ? { slug: "urunler", label: "Ürün", icon: Package } : t,
  );
  const actions = quickActions.filter((a) => !a.module || business.modules[a.module]);

  const tab = (item: (typeof tabs)[number]) => {
    const active = isActive(pathname, item.slug);
    const Icon = item.icon;
    return (
      <Link
        key={item.label}
        href={panelHref(item.slug)}
        aria-current={active ? "page" : undefined}
        className={cn(tabClass, active ? "text-black shadow-[inset_0_3px_0_var(--color-brand)]" : "text-neutral-600")}
      >
        <Icon aria-hidden className="size-6" strokeWidth={active ? 2.4 : 2} />
        {item.label}
      </Link>
    );
  };

  return (
    <nav
      aria-label="Alt menü"
      className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-white pb-[env(safe-area-inset-bottom)] lg:hidden"
    >
      <div className="mx-auto flex max-w-xl items-stretch px-1">
        {tab(tabs[0])}
        {tab(tabs[1])}

        <Sheet open={quickOpen} onOpenChange={setQuickOpen}>
          <SheetTrigger asChild>
            <button type="button" className={cn(tabClass, "text-black")} aria-label="Hızlı işlem">
              <span className="flex size-12 items-center justify-center rounded-full bg-black text-white">
                <Plus aria-hidden className="size-7" />
              </span>
            </button>
          </SheetTrigger>
          <SheetContent side="bottom" className="rounded-t-xl pb-[calc(1rem+env(safe-area-inset-bottom))]">
            <SheetHeader>
              <SheetTitle>Hızlı işlem</SheetTitle>
              <SheetDescription>Ne yapmak istiyorsunuz?</SheetDescription>
            </SheetHeader>
            <div className="grid grid-cols-2 gap-3 px-4">
              {actions.map(({ label, href, icon: Icon }) => (
                <Link
                  key={label}
                  href={href}
                  onClick={() => setQuickOpen(false)}
                  className="flex min-h-20 flex-col items-start justify-center gap-2 rounded-lg border border-border bg-neutral-50 px-4 text-sm font-semibold text-black hover:border-black"
                >
                  <Icon aria-hidden className="size-6 text-black" />
                  {label}
                </Link>
              ))}
            </div>
          </SheetContent>
        </Sheet>

        {tab(tabs[2])}
        {tab(tabs[3])}

        <Sheet open={menuOpen} onOpenChange={setMenuOpen}>
          <SheetTrigger asChild>
            <button type="button" className={cn(tabClass, menuOpen ? "text-black" : "text-neutral-600")}>
              <Menu aria-hidden className="size-6" />
              Menü
            </button>
          </SheetTrigger>
          <SheetContent side="right" className="w-[85%] max-w-xs gap-0 border-sidebar-border bg-sidebar p-0 text-sidebar-foreground">
            <SheetHeader className="border-b border-sidebar-border px-5 py-4">
              <SheetTitle>
                <Wordmark onDark className="text-xl" />
              </SheetTitle>
              <SheetDescription className="sr-only">Panel menüsü</SheetDescription>
            </SheetHeader>
            <div className="flex-1 overflow-y-auto px-3 py-5">
              <NavList onNavigate={() => setMenuOpen(false)} />
            </div>
            <div className="border-t border-sidebar-border px-5 py-4">
              <ApiStatus className="text-sidebar-foreground" />
            </div>
          </SheetContent>
        </Sheet>
      </div>
    </nav>
  );
}
