import { Check } from "lucide-react";
import Image from "next/image";
import Link from "next/link";

import { SiteBrand } from "@/components/brand/mark";
import { company } from "@/content/company";
import { siteSlogan } from "@/content/site";

const assurances = ["Çırak paketi süresiz ücretsiz", "Kredi kartı istemez", "Verileriniz sizin; istediğiniz an Excel"];

/**
 * Giriş/kayıt düzeni (docs/04-tasarim.md, 2026-10-21): masaüstünde solda sıcak açık marka paneli (işaret + yazı, slogan,
 * güvenceler, esnaf fotoğrafı), sağda beyaz form. 768px altında marka paneli üstte ince bant, form tam genişlik; sayfa kaydırılır.
 */
export default function AuthLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col bg-white md:flex-row">
      <section className="flex shrink-0 items-center justify-between border-b border-slate-200 bg-cream px-4 py-3 text-ink md:w-1/2 md:flex-col md:items-start md:justify-between md:border-r md:border-b-0 md:px-12 md:py-10">
        <Link href="/" className="flex min-h-11 items-center rounded-md" aria-label="KobiFabrika ana sayfa">
          <SiteBrand />
        </Link>
        <div className="hidden w-full max-w-md md:block">
          <p className="text-3xl leading-tight font-semibold tracking-tight">{siteSlogan}</p>
          <p className="mt-3 text-base leading-7 text-slate">Paranızı takip eden, müşteri getiren, personelinizi yöneten tek uygulama.</p>
          <ul className="mt-5 flex flex-col gap-2 text-sm">
            {assurances.map((t) => (
              <li key={t} className="flex items-center gap-2">
                <Check aria-hidden className="size-4 shrink-0 text-link" strokeWidth={2.75} /> {t}
              </li>
            ))}
          </ul>
          <Image
            src="/site/foto/kafe.webp"
            alt="Kafesinin tezgâhı önünde kollarını kavuşturmuş duran işletme sahibi."
            width={1000}
            height={750}
            sizes="(min-width: 768px) 420px, 1px"
            className="mt-8 aspect-[4/3] h-auto w-full rounded-2xl object-cover"
          />
        </div>
        <p className="hidden text-xs text-slate md:block">
          {company.name} · Tüm hakları saklıdır.
        </p>
      </section>

      <main className="flex flex-1 items-start justify-center px-4 py-10 md:items-center md:px-12">
        <div className="w-full max-w-sm">{children}</div>
      </main>
    </div>
  );
}
