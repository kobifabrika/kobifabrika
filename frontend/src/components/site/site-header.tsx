"use client";

import { Menu } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

import { Wordmark } from "@/components/brand/wordmark";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { siteNav } from "@/content/site";

/** Tanıtım sitesi üst menüsü: sabit, koyu; telefonda hamburger menü. */
export function SiteHeader() {
  const [open, setOpen] = useState(false);

  return (
    <header className="sticky top-0 z-40 border-b border-border bg-background/80 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4">
        <Link href="/" className="rounded-md" aria-label="KobiFabrika ana sayfa">
          <Wordmark onDark className="text-xl sm:text-2xl" />
        </Link>

        <nav aria-label="Site menüsü" className="hidden items-center gap-1 md:flex">
          {siteNav.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className="flex min-h-11 items-center rounded-md px-3 text-sm font-medium text-neutral-300 transition-colors hover:text-white"
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-1 sm:gap-2">
          <Button asChild variant="ghost" className="hidden text-white sm:inline-flex">
            <Link href="/giris">Giriş yap</Link>
          </Button>
          <Button asChild className="hidden hover:bg-brand-100 sm:inline-flex">
            <Link href="/kayit">Ücretsiz başla</Link>
          </Button>

          <Sheet open={open} onOpenChange={setOpen}>
            <SheetTrigger asChild>
              <Button variant="ghost" size="icon" className="text-white md:hidden" aria-label="Menüyü aç">
                <Menu aria-hidden className="size-6" />
              </Button>
            </SheetTrigger>
            <SheetContent side="right" className="dark w-[85%] max-w-xs gap-0 border-border bg-background p-0 text-foreground">
              <SheetHeader className="border-b border-border px-5 py-4">
                <SheetTitle>
                  <Wordmark onDark className="text-xl" />
                </SheetTitle>
                <SheetDescription className="sr-only">Site menüsü</SheetDescription>
              </SheetHeader>
              <nav aria-label="Site menüsü" className="flex flex-col px-3 py-4">
                {siteNav.map((item) => (
                  <Link
                    key={item.href}
                    href={item.href}
                    onClick={() => setOpen(false)}
                    className="flex min-h-12 items-center rounded-md px-3 text-base font-medium text-neutral-300 hover:bg-muted hover:text-white"
                  >
                    {item.label}
                  </Link>
                ))}
              </nav>
              <div className="mt-auto flex flex-col gap-3 border-t border-border p-5">
                <Button asChild size="lg" className="hover:bg-brand-100">
                  <Link href="/kayit">Ücretsiz başla</Link>
                </Button>
                <Button asChild size="lg" variant="outline">
                  <Link href="/giris">Giriş yap</Link>
                </Button>
              </div>
            </SheetContent>
          </Sheet>
        </div>
      </div>
    </header>
  );
}
