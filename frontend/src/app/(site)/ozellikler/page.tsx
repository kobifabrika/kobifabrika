import { cn } from "cn";
import { ArrowRight, Check } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { BrowserFrame, PhoneFrame } from "@/components/site/device-frames";
import { PageIntro } from "@/components/site/page-intro";
import { Button } from "@/components/ui/button";
import { featureDetails, featureGroups, primaryCta } from "@/content/site";

export const metadata: Metadata = {
  title: "Özellikler",
  description:
    "Satış, veresiye, kasa, stok ve teklif; randevu takvimi, online randevu linki ve WhatsApp hatırlatma; mesai, izin ve prim. KobiFabrika'nın bütün özellikleri.",
  alternates: { canonical: "/ozellikler" },
};

const container = "mx-auto max-w-6xl px-4 sm:px-6";

/**
 * Özellikler sayfası (2026-10-21): ana sayfadaki kısa sekmeli bölümün ayrıntılı hali. Modül başına (Para · Müşteri · Ekip)
 * başlık, çerçevede gerçek ekran ve ayrıntılı özellik listesi; sonra telefon ekranları ve son çağrı. Eski /#ozellikler linki
 * ana sayfada buraya yönlenir (HashRedirect).
 */
export default function FeaturesPage() {
  return (
    <>
      <PageIntro label="Özellikler" title="Defter, ajanda ve hesap makinesi tek uygulamada.">
        Paranı takip eder, müşteri getirir, ekibini yönetir. Telefonda da bilgisayarda da aynı hesap.
      </PageIntro>

      <nav aria-label="Modüller" className="border-b border-neutral-200 bg-white">
        <ul className={cn(container, "flex gap-2 overflow-x-auto py-3")}>
          {featureGroups.map((g) => (
            <li key={g.key}>
              <a
                href={`#${g.key}`}
                className="flex min-h-11 shrink-0 items-center rounded-full border border-neutral-200 px-5 text-[15px] font-semibold text-navy-950 hover:border-navy-950"
              >
                {g.label}
              </a>
            </li>
          ))}
          <li>
            <a
              href="#telefon"
              className="flex min-h-11 shrink-0 items-center rounded-full border border-neutral-200 px-5 text-[15px] font-semibold whitespace-nowrap text-navy-950 hover:border-navy-950"
            >
              Telefonda
            </a>
          </li>
        </ul>
      </nav>

      {featureGroups.map((g, i) => (
        <section
          key={g.key}
          id={g.key}
          aria-labelledby={`baslik-${g.key}`}
          className={cn("scroll-mt-20", i % 2 === 1 && "bg-neutral-100")}
        >
          <div className={cn(container, "py-16 sm:py-20")}>
            <div className="grid items-center gap-10 lg:grid-cols-[1fr_1.35fr] lg:gap-14">
              <div>
                <p className="flex items-center gap-2 text-sm font-semibold text-link">
                  <span aria-hidden className="h-0.5 w-5 rounded-full bg-brand" />
                  {g.label}
                </p>
                <h2
                  id={`baslik-${g.key}`}
                  className="mt-3 text-3xl leading-[1.1] font-semibold tracking-tight sm:text-4xl"
                >
                  {g.title}
                </h2>
                <p className="mt-4 max-w-md text-lg leading-8 text-neutral-600">{g.text}</p>
              </div>
              <div className={cn("rounded-2xl p-3 sm:p-6", i % 2 === 1 ? "bg-white" : "bg-neutral-100")}>
                <BrowserFrame src={g.shot} alt={g.shotAlt} />
              </div>
            </div>
            <ul className="mt-12 grid gap-x-8 gap-y-6 sm:grid-cols-2 lg:grid-cols-3">
              {(featureDetails[g.key] ?? []).map((f) => (
                <li key={f.title} className="flex gap-3">
                  <span className="mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-full bg-coral-soft">
                    <Check aria-hidden className="size-3.5 text-navy-950" strokeWidth={3} />
                  </span>
                  <div>
                    <h3 className="text-lg font-semibold">{f.title}</h3>
                    <p className="mt-1 text-[15px] leading-6 text-neutral-600">{f.text}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </section>
      ))}

      <section id="telefon" aria-labelledby="baslik-telefon" className="scroll-mt-20 bg-cream">
        <div className={cn(container, "grid items-center gap-12 py-16 sm:py-20 lg:grid-cols-2")}>
          <div>
            <p className="flex items-center gap-2 text-sm font-semibold text-link">
              <span aria-hidden className="h-0.5 w-5 rounded-full bg-brand" />
              Telefonda
            </p>
            <h2 id="baslik-telefon" className="mt-3 text-3xl leading-[1.1] font-semibold tracking-tight sm:text-4xl">
              Uygulama indirmeden, telefondan.
            </h2>
            <p className="mt-4 max-w-md text-lg leading-8 text-neutral-600">
              Tarayıcıdan açılır, ana ekrana eklersin. Müşterin de randevusunu kendi telefonundan, uygulama indirmeden
              alır.
            </p>
          </div>
          <div className="flex justify-center gap-4 sm:gap-8 lg:justify-end">
            <PhoneFrame
              className="w-[150px] sm:w-[220px]"
              src="/site/bugun-telefon.webp"
              alt="KobiFabrika telefonda: Bugün ekranında günün özeti ve randevuları."
            />
            <PhoneFrame
              className="w-[150px] sm:w-[220px]"
              src="/site/online-randevu-telefon.webp"
              alt="Müşterinin telefonunda işletmenin online randevu sayfası: hizmet seçimi."
            />
          </div>
        </div>
      </section>

      <section aria-labelledby="son-cagri" className={cn(container, "py-16 sm:py-20")}>
        <div className="rounded-3xl bg-navy-950 p-8 text-white sm:p-12">
          <h2 id="son-cagri" className="max-w-xl text-3xl leading-[1.1] font-semibold tracking-tight sm:text-4xl">
            Hepsini 10 dakikada kur.
          </h2>
          <p className="mt-4 max-w-md text-lg text-neutral-300">Çırak paketi süresiz ücretsiz, kredi kartı istemez.</p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-6">
            <Button asChild size="lg" className="h-12 px-7 text-base">
              <Link href={primaryCta.href}>
                {primaryCta.label} <ArrowRight aria-hidden data-icon="inline-end" />
              </Link>
            </Button>
            <Link
              href="/fiyatlar"
              className="inline-flex min-h-11 items-center text-base font-semibold text-brand underline underline-offset-4 hover:no-underline"
            >
              Paketleri karşılaştır
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}
