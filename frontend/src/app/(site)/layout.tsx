import Link from "next/link";

import { Logo } from "@/components/brand/logo";
import { SiteAnalytics } from "@/components/site/analytics";
import { SiteHeader, StickyCta } from "@/components/site/site-header";
import { SocialIcon } from "@/components/site/social-icons";
import { companyLine } from "@/content/company";
import { legalLinks } from "@/content/legal";
import { footerNav, siteContact, siteSlogan, siteSocial } from "@/content/site";

const footerLink = "flex min-h-11 items-center text-sm text-neutral-300 underline-offset-4 hover:text-white hover:underline";

/**
 * Tanıtım sitesi (docs/04-tasarim.md → "Tanıtım sitesi"): koyu lacivert üst menü ve alt bilgi, beyaz içerik. Telefonda altta
 * sabit "Ücretsiz dene" (içerik altta onun yüksekliği kadar boşluk bırakır). Ziyaretçi istatistiği çerezsiz (SiteAnalytics).
 */
export default function SiteLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col bg-white text-black">
      <SiteHeader />

      <main className="flex-1">{children}</main>

      <footer className="dark border-t border-navy-800 bg-navy-950 pb-24 text-white md:pb-0">
        <div className="mx-auto grid max-w-6xl gap-8 px-4 py-12 sm:grid-cols-2 sm:px-6 lg:grid-cols-5">
          <div className="lg:col-span-2">
            <Logo className="h-5" />
            <p className="mt-4 max-w-sm text-sm text-neutral-300">
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
                    className="flex size-11 items-center justify-center rounded-md text-white hover:bg-navy-800"
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
              {footerNav.map((item) => (
                <li key={item.href}>
                  <Link href={item.href} className={footerLink}>
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
          <nav aria-label="Hukuki metinler">
            <p className="text-sm font-semibold">Hukuki</p>
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
          <div>
            <p className="text-sm font-semibold">İletişim</p>
            <ul className="mt-2 flex flex-col">
              <li>
                <a href={`mailto:${siteContact.email}`} className={footerLink}>
                  {siteContact.email}
                </a>
              </li>
              <li>
                <Link href="/iletisim" className={footerLink}>
                  WhatsApp destek
                </Link>
              </li>
            </ul>
          </div>
        </div>
        <div className="border-t border-navy-800">
          <p className="mx-auto max-w-6xl px-4 py-6 text-xs text-neutral-300 sm:px-6">
            © {companyLine()}. Tüm hakları saklıdır. Bu sitede yalnızca oturum için zorunlu çerez kullanılır;{" "}
            <Link href="/cerez-politikasi" className="underline underline-offset-2 hover:text-white">
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
