import Link from "next/link";

import { Wordmark } from "@/components/brand/wordmark";
import { SiteHeader } from "@/components/site/site-header";
import { SocialIcon } from "@/components/site/social-icons";
import { siteContact, siteNav, siteSlogan, siteSocial } from "@/content/site";

const footerLink =
  "flex min-h-11 items-center text-sm text-neutral-600 underline-offset-4 hover:text-black hover:underline";

/** Tanıtım sitesi — beyaz zemin, siyah yazı (docs/04-tasarim.md → "Tanıtım sitesi"). */
export default function SiteLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col bg-white text-black">
      <SiteHeader />

      <main className="flex-1">{children}</main>

      <footer className="border-t border-neutral-200">
        <div className="mx-auto grid max-w-6xl gap-8 px-4 py-12 sm:grid-cols-2 sm:px-6 lg:grid-cols-4">
          <div className="lg:col-span-2">
            <Wordmark className="text-2xl" />
            <p className="mt-3 max-w-sm text-sm text-neutral-600">
              {siteSlogan}. Esnafın kasası, veresiyesi, randevusu ve personeli tek uygulamada.
            </p>
            <ul aria-label="Sosyal medya" className="mt-4 flex gap-1">
              {siteSocial.map((s) => (
                <li key={s.key}>
                  <a
                    href={s.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    aria-label={`KobiFabrika ${s.label} hesabı (yeni sekmede açılır)`}
                    className="flex size-11 items-center justify-center rounded-md text-black hover:bg-neutral-100"
                  >
                    <SocialIcon name={s.key} className="size-5" />
                  </a>
                </li>
              ))}
            </ul>
          </div>
          <nav aria-label="Alt menü">
            <p className="text-sm font-semibold">Site</p>
            <ul className="mt-2 flex flex-col">
              {siteNav.map((item) => (
                <li key={item.href}>
                  <Link href={item.href} className={footerLink}>
                    {item.label}
                  </Link>
                </li>
              ))}
              <li>
                <Link href="/kvkk" className={footerLink}>
                  KVKK aydınlatma metni
                </Link>
              </li>
            </ul>
          </nav>
          <div>
            <p className="text-sm font-semibold">İletişim</p>
            <ul className="mt-2 flex flex-col">
              <li>
                <a href={`mailto:${siteContact.email}`} className={footerLink}>
                  {siteContact.email}
                </a>
              </li>
              <li className="py-2 text-sm text-neutral-600">Kullanıcılarımıza panelden WhatsApp destek</li>
            </ul>
          </div>
        </div>
        <div className="border-t border-neutral-200">
          <p className="mx-auto max-w-6xl px-4 py-6 text-xs text-neutral-600 sm:px-6">
            © {siteContact.company} Tüm hakları saklıdır.
          </p>
        </div>
      </footer>
    </div>
  );
}
