"use client";

import { cn } from "cn";
import { ArrowLeft, ShieldAlert } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";

import { Logo } from "@/components/brand/logo";
import { QueryError } from "@/components/feedback/query-error";
import { useLogout, useMeQuery } from "@/components/providers/session-provider";
import { TabNav } from "@/components/ui/scroll-row";
import { ApiError } from "@/lib/api/client";

const nav = [
  { href: "/platform", label: "Özet" },
  { href: "/platform/isletmeler", label: "İşletmeler" },
  { href: "/platform/geri-bildirim", label: "Geri bildirim" },
] as const;

/** Menüde etkin bölüm: /platform/isletmeler/[id] → İşletmeler. */
export function activeSection(pathname: string): string {
  return (
    [...nav].reverse().find((n) => pathname === n.href || (n.href !== "/platform" && pathname.startsWith(n.href)))
      ?.href ?? "/platform"
  );
}

/**
 * Süper admin paneli (/platform, sadece KobiFabrika ekibi): işletmeler, paket/pilot/deneme/salt okunur, kredi, notlar, geri
 * bildirimler. İşletme üyeliği ve kurulum aranmaz (PanelGate'ten ayrı). KVKK: işletmelerin müşteri verisi burada yok, sadece sayılar.
 * Asıl yetki kontrolü API'de (PlatformAdmin policy'si); burası sadece ekranı gizler.
 */
export function PlatformShell({ children }: { children: ReactNode }) {
  const { data: me, error, refetch } = useMeQuery();
  const router = useRouter();
  const pathname = usePathname();
  const logout = useLogout();
  const unauthenticated = error instanceof ApiError && error.status === 401;

  useEffect(() => {
    if (unauthenticated) router.replace(`/giris?donus=${encodeURIComponent(pathname)}`);
  }, [unauthenticated, pathname, router]);

  if (error && !unauthenticated && !me)
    return (
      <Centered>
        <p className="text-base font-semibold text-black">Sayfa açılamadı</p>
        <QueryError error={error} onRetry={refetch} className="items-center" />
      </Centered>
    );
  if (!me)
    return (
      <Centered>
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </Centered>
    );
  if (!me.user.isPlatformAdmin)
    return (
      <Centered>
        <ShieldAlert aria-hidden className="size-10 text-neutral-400" />
        <h1 className="text-xl font-bold text-black">Yetkiniz yok</h1>
        <p className="max-w-sm text-sm text-neutral-600">Bu bölüm sadece KobiFabrika destek ekibine açık.</p>
        <Link href="/panel" className="inline-flex min-h-11 items-center font-semibold text-black underline">
          Panele dön
        </Link>
      </Centered>
    );

  const active = activeSection(pathname);
  return (
    <div className="min-h-screen bg-background">
      <header className="dark sticky top-0 z-20 border-b border-sidebar-border bg-background text-foreground">
        <div className="mx-auto flex h-14 max-w-6xl items-center justify-between gap-3 px-4 lg:h-16 lg:px-8">
          <Link href="/platform" className="flex min-h-11 items-center gap-2">
            <Logo surface="dark" priority className="h-4 sm:h-5" />
            <span className="rounded border border-neutral-600 px-1.5 py-0.5 text-xs font-semibold text-white">
              Süper admin
            </span>
          </Link>
          <nav aria-label="Süper admin menüsü" className="hidden items-center gap-1 md:flex">
            {nav.map((n) => (
              <Link
                key={n.href}
                href={n.href}
                aria-current={active === n.href ? "page" : undefined}
                className={cn(
                  "flex min-h-11 items-center rounded-md px-3 text-sm font-semibold",
                  active === n.href ? "bg-navy-800 text-white" : "text-neutral-300 hover:text-white",
                )}
              >
                {n.label}
              </Link>
            ))}
          </nav>
          <div className="flex items-center gap-1">
            <Link
              href="/panel"
              className="flex min-h-11 items-center gap-1 rounded-md px-2 text-sm text-neutral-300 hover:text-white"
            >
              <ArrowLeft aria-hidden className="size-4" />
              <span className="max-sm:sr-only">Panel</span>
            </Link>
            <button
              type="button"
              onClick={() => logout()}
              className="min-h-11 rounded-md px-2 text-sm text-neutral-300 hover:text-white"
            >
              Çıkış
            </button>
          </div>
        </div>
      </header>
      <div className="mx-auto max-w-6xl px-4 pt-3 md:hidden">
        <TabNav
          label="Süper admin menüsü"
          active={active}
          tabs={nav.map((n) => ({ key: n.href, label: n.label, href: n.href }))}
        />
      </div>
      <main className="mx-auto max-w-6xl px-4 py-5 lg:px-8 lg:py-8">{children}</main>
    </div>
  );
}

function Centered({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-3 bg-background px-4 text-center">
      {children}
    </div>
  );
}
