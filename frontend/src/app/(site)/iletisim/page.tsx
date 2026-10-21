import { Mail, MessageCircle } from "lucide-react";
import type { Metadata } from "next";

import { PageIntro } from "@/components/site/page-intro";
import { SocialIcon } from "@/components/site/social-icons";
import { Button } from "@/components/ui/button";
import { company } from "@/content/company";
import { siteSocial } from "@/content/site";
import { supportWhatsappDisplay, supportWhatsappLink } from "@/content/support";

export const metadata: Metadata = {
  title: "İletişim",
  description: "KobiFabrika'ya WhatsApp'tan veya e-postayla ulaşın. Aracısız destek.",
  alternates: { canonical: "/iletisim" },
};

export default function ContactPage() {
  const whatsapp = supportWhatsappLink("Merhaba, KobiFabrika hakkında bilgi almak istiyorum.");
  return (
    <>
      <PageIntro label="İletişim" title="Bize yaz, aynı gün dönelim.">
        Bayi, çağrı merkezi yok; doğrudan ekibe yazarsın. Hafta içi 09:00-18:00 arası yanıtlıyoruz.
      </PageIntro>
      <div className="mx-auto grid max-w-6xl gap-6 px-4 py-12 sm:px-6 sm:py-16 md:grid-cols-2">
        {whatsapp && (
          <section aria-labelledby="whatsapp" className="flex flex-col gap-3 rounded-2xl border border-neutral-200 bg-white p-6">
            <MessageCircle aria-hidden className="size-8 text-link" />
            <h2 id="whatsapp" className="text-xl font-semibold">
              WhatsApp
            </h2>
            <p className="text-base text-neutral-600 tabular-nums">{supportWhatsappDisplay}</p>
            <Button asChild size="lg" className="self-start">
              <a href={whatsapp} target="_blank" rel="noopener noreferrer">
                WhatsApp&apos;tan yaz
              </a>
            </Button>
          </section>
        )}
        <section aria-labelledby="eposta" className="flex flex-col gap-3 rounded-2xl border border-neutral-200 bg-white p-6">
          <Mail aria-hidden className="size-8 text-link" />
          <h2 id="eposta" className="text-xl font-semibold">
            E-posta
          </h2>
          <p className="text-base break-all text-neutral-600">{company.email}</p>
          <Button asChild size="lg" variant="outline" className="self-start">
            <a href={`mailto:${company.email}`}>E-posta gönder</a>
          </Button>
        </section>
        <section aria-labelledby="sosyal" className="md:col-span-2">
          <h2 id="sosyal" className="text-lg font-semibold">
            Sosyal medya
          </h2>
          <ul className="mt-3 flex flex-wrap gap-2">
            {siteSocial.map((s) => (
              <li key={s.key}>
                <a
                  href={s.href}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex min-h-11 items-center gap-2 rounded-md border border-neutral-200 px-4 text-sm font-semibold hover:border-navy-950"
                >
                  <SocialIcon name={s.key} className="size-5" /> {s.label}
                </a>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </>
  );
}
