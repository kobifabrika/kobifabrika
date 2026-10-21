"use client";

import { Menu } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";

import { SiteBrand } from "@/components/brand/mark";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { primaryCta, siteNav } from "@/content/site";

/**
 * Tanıtım sitesi üst menüsü (2026-10-21): beyaz, ince çizgili, sabit. Solda KF işareti + yazı, ortada az sayıda menü, sağda
 * "Giriş yap" ve mavi "Ücretsiz dene". Telefonda işaret + Menü; "Ücretsiz dene" altta sabit (StickyCta).
 */
export function SiteHeader() {
  const [open, setOpen] = useState(false);
  const pathname = usePathname();

  return (
    <header className="sticky top-0 z-40 border-b border-slate-200 bg-white text-ink">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4 sm:px-6">
        <Link href="/" className="flex min-h-11 items-center rounded-md" aria-label="KobiFabrika ana sayfa">
          <SiteBrand />
        </Link>

        <nav aria-label="Site menüsü" className="hidden items-center gap-1 lg:flex">
          {siteNav.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              aria-current={pathname === item.href ? "page" : undefined}
              className="flex min-h-11 items-center rounded-md px-3 text-[15px] font-medium text-slate transition-colors hover:text-ink aria-[current=page]:text-ink aria-[current=page]:underline aria-[current=page]:decoration-brand aria-[current=page]:decoration-2 aria-[current=page]:underline-offset-8"
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-1 sm:gap-2">
          <Link href="/giris" className="hidden min-h-11 items-center rounded-md px-3 text-[15px] font-semibold text-ink hover:text-link sm:flex">
            Giriş yap
          </Link>
          <Button asChild className="hidden px-5 text-ink sm:inline-flex">
            <Link href={primaryCta.href}>{primaryCta.label}</Link>
          </Button>

          <Sheet open={open} onOpenChange={setOpen}>
            <SheetTrigger asChild>
              <button
                type="button"
                className="flex size-11 items-center justify-center rounded-md text-ink hover:bg-mist lg:hidden"
                aria-label="Menü"
              >
                <Menu aria-hidden className="size-6" />
              </button>
            </SheetTrigger>
            <SheetContent side="right" className="w-[85%] max-w-xs gap-0 border-slate-200 bg-white p-0 text-ink">
              <SheetHeader className="border-b border-slate-200 px-5 py-4">
                <SheetTitle>
                  <SiteBrand />
                </SheetTitle>
                <SheetDescription className="sr-only">Site menüsü</SheetDescription>
              </SheetHeader>
              <nav aria-label="Site menüsü" className="flex flex-col px-3 py-4">
                {siteNav.map((item) => (
                  <Link
                    key={item.href}
                    href={item.href}
                    onClick={() => setOpen(false)}
                    className="flex min-h-12 items-center rounded-md px-3 text-base font-medium text-ink hover:bg-mist"
                  >
                    {item.label}
                  </Link>
                ))}
              </nav>
              <div className="mt-auto flex flex-col gap-3 border-t border-slate-200 p-5">
                <Button asChild size="lg" className="text-ink">
                  <Link href={primaryCta.href} onClick={() => setOpen(false)}>
                    {primaryCta.label}
                  </Link>
                </Button>
                <Button asChild size="lg" variant="outline">
                  <Link href="/giris" onClick={() => setOpen(false)}>
                    Giriş yap
                  </Link>
                </Button>
              </div>
            </SheetContent>
          </Sheet>
        </div>
      </div>
    </header>
  );
}

/** Telefonda sayfanın altında sabit "Ücretsiz dene" (md ve üstünde yok; üst menüde var). */
export function StickyCta() {
  return (
    <div className="fixed inset-x-0 bottom-0 z-30 border-t border-slate-200 bg-white px-4 pt-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))] md:hidden">
      <Button asChild size="lg" className="w-full text-ink">
        <Link href={primaryCta.href}>{primaryCta.label} · Çırak ücretsiz</Link>
      </Button>
    </div>
  );
}
