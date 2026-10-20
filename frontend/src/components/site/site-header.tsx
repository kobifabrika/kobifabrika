"use client";

import { Menu } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";

import { Logo } from "@/components/brand/logo";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { primaryCta, siteNav } from "@/content/site";

/**
 * Tanıtım sitesi üst menüsü: koyu lacivert (logo koyu zeminde; docs/04), sabit. Masaüstünde menü + Giriş + "Ücretsiz dene"
 * (mavi, ana çağrı); telefonda logo + Menü (alttan sabit "Ücretsiz dene" ayrıca StickyCta'da).
 */
export function SiteHeader() {
  const [open, setOpen] = useState(false);
  const pathname = usePathname();

  return (
    <header className="dark sticky top-0 z-40 border-b border-navy-800 bg-navy-950 text-white">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4 sm:px-6">
        <Link href="/" className="flex min-h-11 items-center rounded-md" aria-label="KobiFabrika ana sayfa">
          <Logo priority className="h-[18px] sm:h-5" />
        </Link>

        <nav aria-label="Site menüsü" className="hidden items-center gap-1 lg:flex">
          {siteNav.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              aria-current={pathname === item.href ? "page" : undefined}
              className="flex min-h-11 items-center rounded-md px-3 text-sm font-medium text-neutral-300 transition-colors hover:text-white aria-[current=page]:text-white"
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-1 sm:gap-2">
          <Link href="/giris" className="hidden min-h-11 items-center rounded-md px-3 text-sm font-semibold text-white hover:text-brand sm:flex">
            Giriş yap
          </Link>
          <Button asChild className="hidden sm:inline-flex">
            <Link href={primaryCta.href}>{primaryCta.label}</Link>
          </Button>

          <Sheet open={open} onOpenChange={setOpen}>
            <SheetTrigger asChild>
              <button
                type="button"
                className="flex size-11 items-center justify-center rounded-md text-white hover:bg-navy-800 lg:hidden"
                aria-label="Menü"
              >
                <Menu aria-hidden className="size-6" />
              </button>
            </SheetTrigger>
            <SheetContent side="right" className="dark w-[85%] max-w-xs gap-0 border-navy-800 bg-navy-950 p-0 text-white">
              <SheetHeader className="border-b border-navy-800 px-5 py-4">
                <SheetTitle>
                  <Logo className="h-5" />
                </SheetTitle>
                <SheetDescription className="sr-only">Site menüsü</SheetDescription>
              </SheetHeader>
              <nav aria-label="Site menüsü" className="flex flex-col px-3 py-4">
                {siteNav.map((item) => (
                  <Link
                    key={item.href}
                    href={item.href}
                    onClick={() => setOpen(false)}
                    className="flex min-h-12 items-center rounded-md px-3 text-base font-medium text-white hover:bg-navy-800"
                  >
                    {item.label}
                  </Link>
                ))}
              </nav>
              <div className="mt-auto flex flex-col gap-3 border-t border-navy-800 p-5">
                <Button asChild size="lg">
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
    <div className="fixed inset-x-0 bottom-0 z-30 border-t border-navy-800 bg-navy-950 px-4 pt-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))] md:hidden">
      <Button asChild size="lg" className="w-full">
        <Link href={primaryCta.href}>{primaryCta.label} · Çırak ücretsiz</Link>
      </Button>
    </div>
  );
}
