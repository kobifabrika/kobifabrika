import Link from "next/link";

import { Wordmark } from "@/components/brand/wordmark";
import { SiteHeader } from "@/components/site/site-header";
import { siteContact, siteNav } from "@/content/site";

/** Tanıtım sitesi — koyu tema (docs/04-tasarim.md → "Site ve kimlik ekranları koyu tema"). */
export default function SiteLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="dark flex min-h-screen flex-col bg-background text-foreground">
      <SiteHeader />

      <main className="flex-1">{children}</main>

      <footer className="border-t border-border">
        <div className="mx-auto grid max-w-6xl gap-8 px-4 py-12 sm:grid-cols-2 lg:grid-cols-4">
          <div className="lg:col-span-2">
            <Wordmark onDark className="text-2xl" />
            <p className="mt-3 max-w-sm text-sm text-neutral-400">
              KOBİ ve esnafın parasını takip eden, müşteri getiren ve personelini yöneten tek uygulama.
            </p>
          </div>
          <nav aria-label="Alt menü">
            <p className="text-sm font-semibold text-white">Site</p>
            <ul className="mt-2 flex flex-col">
              {siteNav.map((item) => (
                <li key={item.href}>
                  <Link href={item.href} className="flex min-h-11 items-center text-sm text-neutral-400 hover:text-white">
                    {item.label}
                  </Link>
                </li>
              ))}
              <li>
                <Link href="/kvkk" className="flex min-h-11 items-center text-sm text-neutral-400 hover:text-white">
                  KVKK aydınlatma metni
                </Link>
              </li>
            </ul>
          </nav>
          <div>
            <p className="text-sm font-semibold text-white">İletişim</p>
            <ul className="mt-2 flex flex-col">
              <li>
                <a
                  href={`mailto:${siteContact.email}`}
                  className="flex min-h-11 items-center text-sm text-brand-300 hover:text-brand-100"
                >
                  {siteContact.email}
                </a>
              </li>
              <li className="py-2 text-sm text-neutral-400">Kullanıcılarımıza panelden WhatsApp destek</li>
            </ul>
          </div>
        </div>
        <div className="border-t border-border">
          <p className="mx-auto max-w-6xl px-4 py-6 text-xs text-neutral-400">
            © {siteContact.company} Tüm hakları saklıdır.
          </p>
        </div>
      </footer>
    </div>
  );
}
