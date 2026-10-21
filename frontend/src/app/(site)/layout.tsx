import Link from "next/link";

import { SiteBrand } from "@/components/brand/mark";
import { SiteAnalytics } from "@/components/site/analytics";
import { SiteHeader, StickyCta } from "@/components/site/site-header";
import { SocialIcon } from "@/components/site/social-icons";
import { companyLine } from "@/content/company";
import { legalLinks } from "@/content/legal";
import { footerColumns, siteContact, siteSlogan, siteSocial } from "@/content/site";

const footerLink = "flex min-h-11 items-center text-sm text-slate underline-offset-4 hover:text-ink hover:underline";

/**
 * Tanıtım sitesi (docs/04-tasarim.md → "Tanıtım sitesi", 2026-10-21): beyaz üst menü, açık sis zeminli kolonlu alt bilgi
 * (Stripe tarzı), yazı lacivert mürekkep. Telefonda altta sabit "Ücretsiz dene" (içerik altta onun yüksekliği kadar boşluk
 * bırakır). Ziyaretçi istatistiği çerezsiz (SiteAnalytics).
 */
export default function SiteLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col bg-white text-ink">
      <SiteHeader />

      <main className="flex-1">{children}</main>

      <footer className="border-t border-slate-200 bg-mist pb-24 md:pb-0">
        <div className="mx-auto grid max-w-6xl gap-x-8 gap-y-10 px-4 py-14 sm:grid-cols-2 sm:px-6 lg:grid-cols-[1.6fr_repeat(4,1fr)]">
          <div>
            <SiteBrand />
            <p className="mt-4 max-w-xs text-sm leading-6 text-slate">
              {siteSlogan}. Esnafın kasası, veresiyesi, randevusu ve personeli tek uygulamada.
            </p>
            <a href={`mailto:${siteContact.email}`} className="mt-3 inline-flex min-h-11 items-center text-sm font-semibold text-link underline-offset-4 hover:underline">
              {siteContact.email}
            </a>
            <ul aria-label="Sosyal medya" className="mt-2 flex gap-1">
              {siteSocial.map((s) => (
                <li key={s.key}>
                  <a
                    href={s.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    aria-label={`KobiFabrika ${s.label} hesabı (yeni sekmede açılır)`}
                    className="flex size-11 items-center justify-center rounded-md text-ink hover:bg-white"
                  >
                    <SocialIcon name={s.key} className="size-5" />
                  </a>
                </li>
              ))}
            </ul>
          </div>
          {footerColumns.map((col) => (
            <nav key={col.title} aria-label={col.title}>
              <p className="text-sm font-semibold text-ink">{col.title}</p>
              <ul className="mt-2 flex flex-col">
                {col.links.map((item) => (
                  <li key={item.href}>
                    <Link href={item.href} className={footerLink}>
                      {item.label}
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>
          ))}
          <nav aria-label="Hukuki metinler">
            <p className="text-sm font-semibold text-ink">Hukuki</p>
            <ul className="mt-2 flex flex-col">
              {legalLinks.map((item) => (
                <li key={item.href}>
                  <Link href={item.href} className={footerLink}>
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        </div>
        <div className="border-t border-slate-200">
          <p className="mx-auto max-w-6xl px-4 py-6 text-xs leading-5 text-slate sm:px-6">
            © {companyLine()}. Tüm hakları saklıdır. Bu sitede yalnızca oturum için zorunlu çerez kullanılır;{" "}
            <Link href="/cerez-politikasi" className="underline underline-offset-2 hover:text-ink">
              çerez politikası
            </Link>
            .
          </p>
        </div>
      </footer>
      <StickyCta />
      <SiteAnalytics />
    </div>
  );
}
