import { Check } from "lucide-react";
import Link from "next/link";

import { Wordmark } from "@/components/brand/wordmark";
import { company } from "@/content/company";
import { siteSlogan } from "@/content/site";

const assurances = ["Çırak paketi süresiz ücretsiz", "Kredi kartı istemez", "Verileriniz sizin; istediğiniz an Excel"];

/**
 * Giriş/kayıt düzeni (docs/04-tasarim.md, 2026-10-21): masaüstünde solda koyu marka paneli (sitenin mürekkep rengi #0a1f3c;
 * büyük "KobiFabrika" yazısı, slogan, üç güvence; fotoğraf yok), sağda beyaz form. 768px altında marka paneli üstte ince bant
 * (sadece yazı), form tam genişlik; sayfa kaydırılır.
 */
export default function AuthLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col bg-white md:flex-row">
      <section className="dark relative flex shrink-0 items-center bg-ink px-4 py-2 text-white md:w-1/2 md:flex-col md:justify-center md:px-12 md:py-12">
        <div className="w-full md:max-w-md">
          <Link href="/" className="inline-flex min-h-11 items-center rounded-md" aria-label="KobiFabrika ana sayfa">
            <Wordmark onDark className="text-xl md:text-5xl" />
          </Link>
          <div className="hidden md:block">
            <p className="mt-6 text-2xl leading-tight font-semibold tracking-tight">{siteSlogan}</p>
            <p className="mt-3 text-base leading-7 text-slate-300">
              Paranızı takip eden, müşteri getiren, personelinizi yöneten tek uygulama.
            </p>
            <ul className="mt-8 flex flex-col gap-3 text-base">
              {assurances.map((t) => (
                <li key={t} className="flex items-center gap-3">
                  <Check aria-hidden className="size-5 shrink-0 text-brand" strokeWidth={2.75} /> {t}
                </li>
              ))}
            </ul>
          </div>
        </div>
        <p className="hidden text-xs text-slate-300 md:absolute md:inset-x-12 md:bottom-8 md:block">
          {company.name} · Tüm hakları saklıdır.
        </p>
      </section>

      <main className="flex flex-1 items-start justify-center px-4 py-10 md:items-center md:px-12">
        <div className="w-full max-w-sm">{children}</div>
      </main>
    </div>
  );
}
