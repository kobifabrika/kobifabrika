import { MessageCircle } from "lucide-react";
import type { Metadata } from "next";
import { notFound } from "next/navigation";

import { Logo } from "@/components/brand/logo";
import { Button } from "@/components/ui/button";
import { siteUrl } from "@/content/seo";
import { isSiteOnly, pilotCtaLabel, pilotLink } from "@/content/site-mode";

export const metadata: Metadata = {
  title: "Çok yakında",
  description: "KobiFabrika paneli pilot işletmelerle açılıyor.",
  robots: { index: false, follow: false },
};

/**
 * Sadece-site modunda (content/site-mode.ts) giriş, kayıt, panel ve link sayfalarının yerine (proxy.ts); app. ve api. alt
 * alanları da bunu gösterir (Caddy). Bağlantılar tam adresle: alt alandaki "/" yine bu sayfaya düşer. Mod kapalıyken 404.
 */
export default function ComingSoonPage() {
  if (!isSiteOnly()) notFound();
  return (
    <div className="flex min-h-screen flex-col bg-white text-navy-950">
      <header className="dark bg-navy-950">
        <div className="mx-auto flex h-16 max-w-6xl items-center px-4 sm:px-6">
          <a href={siteUrl} className="flex min-h-11 items-center rounded-md" aria-label="KobiFabrika ana sayfa">
            <Logo surface="dark" priority />
          </a>
        </div>
      </header>
      <main className="flex-1">
        <div className="mx-auto flex w-full max-w-xl flex-col gap-4 px-4 py-12 sm:py-16">
          <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">Çok yakında</h1>
          <p className="text-base leading-7 text-neutral-600">
            KobiFabrika paneli önce pilot işletmelerle açılıyor. Kasa, veresiye, randevu ve personel tek uygulamada; pilot
            listesine katılan esnafa ilk biz haber veriyoruz.
          </p>
          <div className="mt-2 flex flex-col gap-3 sm:flex-row">
            <Button asChild size="lg">
              <a href={pilotLink()}>
                <MessageCircle aria-hidden data-icon="inline-start" /> {pilotCtaLabel}
              </a>
            </Button>
            <Button asChild size="lg" variant="outline">
              <a href={siteUrl}>Tanıtım sitesine dön</a>
            </Button>
          </div>
        </div>
      </main>
    </div>
  );
}
