import { cn } from "cn";
import { ArrowRight, Check } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import type { ReactNode } from "react";

import { LaptopFrame, PhoneFrame } from "@/components/site/device-frames";
import { FaqList } from "@/components/site/faq-list";
import { Pricing } from "@/components/site/pricing";
import { SectorIcon } from "@/components/site/sector-icons";
import { Button } from "@/components/ui/button";
import { company } from "@/content/company";
import { plans } from "@/content/plans";
import { differentiators, featureGroups, primaryCta, sectors, siteDescription, siteSlogan, steps } from "@/content/site";

// Tasarım: docs/04-tasarim.md → "Tanıtım sitesi" (5 renk; karar 2026-10-17). Koyu bölümler #000a1d / #00385a; beyazda mavi yazı yok.

export const metadata: Metadata = {
  title: { absolute: `KobiFabrika — ${siteSlogan}` },
  description: siteDescription,
  alternates: { canonical: "/" },
};

const container = "mx-auto max-w-6xl px-4 sm:px-6";

function SectionHeading({ id, label, title, text, onDark = false }: { id: string; label: string; title: ReactNode; text?: string; onDark?: boolean }) {
  return (
    <div className="max-w-2xl">
      <p className={cn("text-sm font-semibold", onDark ? "text-cyan" : "text-link")}>{label}</p>
      <h2 id={id} className="mt-3 text-3xl leading-[1.1] font-bold tracking-tight sm:text-5xl">
        {title}
      </h2>
      {text && <p className={cn("mt-5 text-lg", onDark ? "text-neutral-300" : "text-neutral-700")}>{text}</p>}
    </div>
  );
}

const jsonLd = [
  {
    "@context": "https://schema.org",
    "@type": "Organization",
    name: company.brand,
    url: company.website,
    email: company.email,
    logo: `${company.website}/icon.png`,
    sameAs: ["https://www.instagram.com/kobifabrika", "https://www.facebook.com/kobifabrika", "https://www.youtube.com/@kobifabrika", "https://www.tiktok.com/@kobifabrika"],
  },
  {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: "KobiFabrika",
    applicationCategory: "BusinessApplication",
    operatingSystem: "Web",
    description: siteDescription,
    offers: plans.map((p) => ({
      "@type": "Offer",
      name: p.name,
      price: p.monthlyPrice,
      priceCurrency: "TRY",
      description: p.tagline,
    })),
  },
];

export default function HomePage() {
  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }} />

      {/* 1) Giriş: koyu lacivert, başlık, çağrı, gerçek ekranlar */}
      <section aria-labelledby="baslik" className="dark bg-navy-950 text-white">
        {/* Belirme animasyonu yok: ilk ekranın en büyük öğesi (çağrı düğmesi) geç "boyanmış" sayılıyordu (Lighthouse LCP). */}
        <div className={cn(container, "pt-12 pb-16 sm:pt-20 lg:pb-24")}>
          <p className="text-sm font-semibold text-cyan">{siteSlogan}</p>
          <h1 id="baslik" className="mt-4 max-w-4xl text-[2.4rem] leading-[1.04] font-bold tracking-[-0.035em] sm:text-6xl lg:text-7xl">
            Kasan, veresiyen, randevun ve personelin tek uygulamada.
          </h1>
          <p className="mt-6 max-w-2xl text-lg text-neutral-300 sm:text-xl">
            Esnaf ve küçük işletme için: telefondan kullanılır, 10 dakikada kurulur. Müşterin randevusunu kendisi alır, borcu tek
            dokunuşla hatırlatılır.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-6">
            <Button asChild size="lg" className="sm:px-6">
              <Link href={primaryCta.href}>
                {primaryCta.label} <ArrowRight aria-hidden data-icon="inline-end" />
              </Link>
            </Button>
            <Link href="/fiyatlar" className="inline-flex min-h-11 items-center text-base font-semibold text-brand underline underline-offset-4 hover:no-underline">
              Fiyatları gör
            </Link>
          </div>
          <ul className="mt-6 flex flex-wrap gap-x-6 gap-y-2 text-sm text-neutral-300">
            {["Çırak paketi süresiz ücretsiz", "Kredi kartı istemez", "Verin senin, Excel'e aktarırsın"].map((t) => (
              <li key={t} className="flex items-center gap-2">
                <Check aria-hidden className="size-4 text-cyan" strokeWidth={2.5} /> {t}
              </li>
            ))}
          </ul>

          <div className="mt-14 flex items-end gap-6 lg:mt-16">
            <LaptopFrame
              className="hidden flex-1 md:block [&_figcaption]:text-neutral-300"
              src="/site/bugun-masaustu.webp"
              alt="KobiFabrika Bugün ekranı: günün randevuları, yarının randevuları, satış ve tahsilat kartları."
            />
            <PhoneFrame
              priority
              dark
              className="mx-auto md:mx-0"
              src="/site/bugun-telefon.webp"
              alt="KobiFabrika telefonda: Bugün ekranında hızlı işlem düğmeleri ve günün randevuları."
            />
          </div>
        </div>
      </section>

      {/* 2) Kimler için */}
      <section aria-labelledby="kimler" className="border-b border-neutral-200">
        <div className={cn(container, "py-20 sm:py-24")}>
          <SectionHeading id="kimler" label="Kimler için" title="Hangi işi yapıyorsan ona göre açılır." text="Kurulumda sektörünü seçersin; menü, hizmet listesi ve ekranlar ona göre gelir." />
          <ul className="mt-12 grid gap-px overflow-hidden rounded-lg border border-neutral-200 bg-neutral-200 sm:grid-cols-2 lg:grid-cols-5">
            {sectors.map((s) => (
              <li key={s.title} className="flex flex-col gap-3 bg-white p-6">
                <SectorIcon name={s.icon} className="size-12 text-link" />
                <h3 className="text-lg font-semibold">{s.title}</h3>
                <p className="text-sm leading-6 text-neutral-700">{s.text}</p>
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* 3) Özellikler: para, müşteri, ekip */}
      <section aria-labelledby="ozellikler">
        <div className={cn(container, "pt-20 sm:pt-28")}>
          <SectionHeading id="ozellikler" label="Özellikler" title="Defter, ajanda ve hesap makinesi tek yerde." text="Paranı takip eder, müşteri getirir, ekibini yönetir." />
        </div>
        <ol className={cn(container, "mt-12 flex flex-col pb-20 sm:pb-28")}>
          {featureGroups.map((f, i) => (
            <li key={f.key} className="grid gap-8 border-t-2 border-navy-950 py-12 sm:py-16 lg:grid-cols-[1fr_1.4fr] lg:gap-16">
              <div>
                <p className="flex items-center gap-3 text-sm font-semibold text-link tabular-nums">
                  <span className="flex size-8 items-center justify-center rounded-full bg-link text-white">{i + 1}</span> {f.label}
                </p>
                <h3 className="mt-4 text-3xl leading-tight font-bold tracking-tight sm:text-4xl">{f.title}</h3>
                <p className="mt-4 max-w-md text-base leading-7 text-neutral-700">{f.text}</p>
                <ul className="mt-6 flex flex-col gap-2.5">
                  {f.points.map((p) => (
                    <li key={p} className="flex items-center gap-2.5 text-base">
                      <Check aria-hidden className="size-4 shrink-0 text-link" strokeWidth={2.5} /> {p}
                    </li>
                  ))}
                </ul>
              </div>
              <LaptopFrame src={f.shot} alt={f.shotAlt} className="w-full lg:justify-self-end" />
            </li>
          ))}
        </ol>
      </section>

      {/* 4) Bizi farklı kılan: koyu lacivert */}
      <section aria-labelledby="farkimiz" className="dark bg-navy-950 text-white">
        <div className={cn(container, "py-20 sm:py-28")}>
          <SectionHeading id="farkimiz" label="Farkımız" onDark title="Esnafın derdini dinleyerek yaptık." />
          <ol className="mt-12 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {differentiators.map((d, i) => (
              <li key={d.title} className="rounded-lg bg-navy-800 p-6">
                <p className="text-sm font-semibold text-cyan tabular-nums">{String(i + 1).padStart(2, "0")}</p>
                <h3 className="mt-2 text-lg font-semibold">{d.title}</h3>
                <p className="mt-1 text-sm leading-6 text-neutral-300">{d.text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* 5) Nasıl çalışır */}
      <section aria-labelledby="nasil" className="border-b border-neutral-200">
        <div className={cn(container, "py-20 sm:py-28")}>
          <SectionHeading id="nasil" label="Nasıl çalışır" title="Üç adımda başla." />
          <ol className="mt-12 grid gap-10 lg:grid-cols-3">
            {steps.map((s, i) => (
              <li key={s.title} className="border-t-2 border-link pt-6">
                <p className="font-heading text-6xl font-bold text-link tabular-nums">{i + 1}</p>
                <h3 className="mt-3 text-xl font-semibold">{s.title}</h3>
                <p className="mt-2 text-base leading-7 text-neutral-700">{s.text}</p>
              </li>
            ))}
          </ol>
          <div className="mt-12 flex flex-col items-start gap-6 rounded-lg bg-neutral-50 p-6 sm:flex-row sm:items-center">
            <PhoneFrame caption={false} className="w-[160px] sm:w-[180px]" src="/site/online-randevu-telefon.webp" alt="Müşterinin telefonunda işletmenin online randevu sayfası: hizmet seçimi." />
            <div>
              <h3 className="text-xl font-semibold">Müşterin randevusunu kendisi alır</h3>
              <p className="mt-2 max-w-md text-base leading-7 text-neutral-700">
                İşletmene özel randevu linkini Instagram profiline, Google işletme kaydına ve WhatsApp durumuna koy. Talep gelince
                onaylarsın, müşterine WhatsApp&apos;tan haber verirsin.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* 6) Fiyatlar */}
      <section aria-labelledby="fiyatlar">
        <div className={cn(container, "py-20 sm:py-28")}>
          <SectionHeading id="fiyatlar" label="Fiyatlar" title="Ücretsiz başla, işin büyüyünce geç." text="TL, sabit ve komisyonsuz fiyat. Kalfa ve Usta'yı 14 gün ücretsiz dene." />
          <div className="mt-10">
            <Pricing />
          </div>
        </div>
      </section>

      {/* 7) SSS */}
      <section aria-labelledby="sss" className="border-t border-neutral-200">
        <div className={cn(container, "grid gap-10 py-20 sm:py-28 lg:grid-cols-[1fr_1.6fr]")}>
          <div>
            <SectionHeading id="sss" label="SSS" title="Sık sorulan sorular" />
            <Link href="/sss" className="mt-6 inline-flex min-h-11 items-center font-semibold text-link underline underline-offset-4">
              Bütün sorular
            </Link>
          </div>
          <FaqList limit={5} />
        </div>
      </section>

      {/* 8) Son çağrı */}
      <section aria-labelledby="son-cagri" className="dark bg-navy-800 text-white">
        <div className={cn(container, "py-20 sm:py-24")}>
          <h2 id="son-cagri" className="max-w-3xl text-4xl leading-[1.05] font-bold tracking-[-0.035em] sm:text-6xl">
            Defteri bugün kapat.
          </h2>
          <p className="mt-5 max-w-xl text-lg text-neutral-300">10 dakikada kur, Çırak paketiyle ücretsiz kullan. Kredi kartı istemez.</p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-6">
            <Button asChild size="lg" className="sm:px-6">
              <Link href={primaryCta.href}>
                {primaryCta.label} <ArrowRight aria-hidden data-icon="inline-end" />
              </Link>
            </Button>
            <Link href="/iletisim" className="inline-flex min-h-11 items-center text-base font-semibold text-brand underline underline-offset-4 hover:no-underline">
              Soru sor
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}
