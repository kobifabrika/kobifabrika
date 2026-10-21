import Link from "next/link";

import { Logo } from "@/components/brand/logo";
import { company } from "@/content/company";
import { siteSlogan } from "@/content/site";

/**
 * Giriş/kayıt düzeni (docs/04-tasarim.md): masaüstünde solda koyu marka paneli, sağda form.
 * 768px altında marka paneli üstte ince bant olur, form tam genişlik; sayfa kaydırılabilir.
 */
export default function AuthLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col bg-white md:flex-row">
      <section className="relative flex shrink-0 items-center justify-between bg-navy-950 px-4 py-4 text-white md:w-1/2 md:flex-col md:justify-center md:px-12 md:py-12">
        <Link href="/" className="flex min-h-11 items-center rounded-md outline-sidebar-ring" aria-label="KobiFabrika ana sayfa">
          <Logo priority className="h-5 md:h-9" />
        </Link>
        <p className="hidden max-w-sm text-center text-base text-sidebar-foreground md:mt-5 md:block">
          <span className="block text-xl font-semibold text-white">{siteSlogan}</span>
          <span className="mt-2 block">Paranızı takip eden, müşteri getiren, personelinizi yöneten tek uygulama.</span>
        </p>
        <div className="hidden text-center text-xs text-sidebar-foreground/80 md:absolute md:inset-x-12 md:bottom-8 md:block">
          <p className="font-semibold tracking-wider">{company.name}</p>
          <p className="mt-1">Tüm hakları saklıdır.</p>
        </div>
      </section>

      <main className="flex flex-1 items-start justify-center px-4 py-10 md:items-center md:px-12">
        <div className="w-full max-w-sm">{children}</div>
      </main>
    </div>
  );
}
