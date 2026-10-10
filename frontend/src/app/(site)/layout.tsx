import Link from "next/link";

import { Wordmark } from "@/components/brand/wordmark";
import { Button } from "@/components/ui/button";

export default function SiteLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col bg-white">
      <header className="sticky top-0 z-20 border-b border-border bg-white/95 backdrop-blur">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4">
          <Link href="/" className="rounded-md" aria-label="KobiFabrika ana sayfa">
            <Wordmark className="text-xl sm:text-2xl" />
          </Link>
          <nav className="flex items-center gap-1 sm:gap-2">
            <Button asChild variant="ghost" className="text-brand-900">
              <Link href="/giris">Giriş yap</Link>
            </Button>
            <Button asChild>
              <Link href="/kayit">Ücretsiz başla</Link>
            </Button>
          </nav>
        </div>
      </header>

      <main className="flex-1">{children}</main>

      <footer className="bg-brand-900 text-sidebar-foreground">
        <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-8 text-sm sm:flex-row sm:items-center sm:justify-between">
          <Wordmark onDark className="text-lg" />
          <p>© KobiFabrika Yazılım A.Ş. Tüm hakları saklıdır.</p>
        </div>
      </footer>
    </div>
  );
}
