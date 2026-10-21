import { cn } from "cn";
import { ArrowRight, Check, MessageCircle, Smartphone, Link2 } from "lucide-react";
import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import type { ReactNode } from "react";

import { PhoneFrame, softShadow } from "@/components/site/device-frames";
import { FaqList } from "@/components/site/faq-list";
import { FeatureTabs } from "@/components/site/feature-tabs";
import { HashRedirect } from "@/components/site/hash-redirect";
import { Pricing } from "@/components/site/pricing";
import { Button } from "@/components/ui/button";
import { company } from "@/content/company";
import { plans } from "@/content/plans";
import {
  differentiators,
  featureGroups,
  homeTitle,
  primaryCta,
  sectors,
  siteDescription,
  siteSlogan,
  steps,
} from "@/content/site";

// Tasarım: docs/04-tasarim.md → "Tanıtım sitesi" (2026-10-21; ikas + Stripe + Paraşüt yönü). Renk geçişi sadece ilk ekranda.

export const metadata: Metadata = {
  title: { absolute: homeTitle },
  openGraph: { title: homeTitle, description: siteDescription },
  description: siteDescription,
  alternates: { canonical: "/" },
};

const container = "mx-auto max-w-6xl px-4 sm:px-6";

function SectionHeading({
  id,
  label,
  title,
  text,
  center = false,
}: {
  id: string;
  label: string;
  title: ReactNode;
  text?: string;
  center?: boolean;
}) {
  return (
    <div className={cn("max-w-2xl", center && "mx-auto text-center")}>
      <p className={cn("flex items-center gap-2 text-sm font-semibold text-link", center && "justify-center")}>
        <span aria-hidden className="h-0.5 w-5 rounded-full bg-brand" />
        {label}
      </p>
      <h2 id={id} className="mt-3 text-3xl leading-[1.1] font-semibold tracking-tight sm:text-[2.75rem]">
        {title}
      </h2>
      {text && <p className="mt-4 text-lg leading-8 text-slate">{text}</p>}
    </div>
  );
}

const assurances = [
  "Çırak paketi süresiz ücretsiz",
  "Kredi kartı istemez",
  "10 dakikada kurulur",
  "Aracısız WhatsApp destek",
];

const jsonLd = [
  {
    "@context": "https://schema.org",
    "@type": "Organization",
    name: company.brand,
    url: company.website,
    email: company.email,
    logo: `${company.website}/icon.png`,
    sameAs: [
      "https://www.instagram.com/kobifabrika",
      "https://www.facebook.com/kobifabrika",
      "https://www.youtube.com/@kobifabrika",
      "https://www.tiktok.com/@kobifabrika",
    ],
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
      <HashRedirect hash="#ozellikler" to="/ozellikler" />

      {/* 1) Giriş: ölçülü renk geçişi, başlık, çağrı, esnaf fotoğrafı + telefondaki gerçek ekran */}
      <section aria-labelledby="baslik" className="border-b border-slate-200 hero-glow">
        <div
          className={cn(
            container,
            "grid items-center gap-12 pt-10 pb-14 sm:pt-16 lg:grid-cols-[1.05fr_1fr] lg:gap-10 lg:pt-20 lg:pb-20",
          )}
        >
          <div>
            <p className="inline-flex items-center rounded-full border border-slate-200 bg-white px-3 py-1 text-sm font-semibold text-ink">
              {siteSlogan}
            </p>
            <h1
              id="baslik"
              className="mt-5 text-[2.35rem] leading-[1.05] font-semibold tracking-[-0.03em] sm:text-6xl lg:text-[4.1rem]"
            >
              Kasan, veresiyen, randevun ve personelin <span className="text-link">tek uygulamada.</span>
            </h1>
            <p className="mt-6 max-w-xl text-lg leading-8 text-slate sm:text-xl">
              Esnaf ve küçük işletme için: telefondan kullanılır, 10 dakikada kurulur. Müşterin randevusunu kendisi
              alır, borcu tek dokunuşla hatırlatılır.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center">
              <Button asChild size="lg" className="h-12 px-7 text-base text-ink">
                <Link href={primaryCta.href}>
                  {primaryCta.label} <ArrowRight aria-hidden data-icon="inline-end" />
                </Link>
              </Button>
              <Button
                asChild
                size="lg"
                variant="outline"
                className="h-12 border-slate-200 bg-white px-7 text-base text-ink"
              >
                <Link href="/fiyatlar">Fiyatları gör</Link>
              </Button>
            </div>
            <ul className="mt-7 grid gap-x-6 gap-y-2 text-sm text-ink sm:grid-cols-2">
              {assurances.map((t) => (
                <li key={t} className="flex items-center gap-2">
                  <Check aria-hidden className="size-4 shrink-0 text-link" strokeWidth={2.75} /> {t}
                </li>
              ))}
            </ul>
          </div>

          <div className="relative mx-auto w-full max-w-[560px] pb-10 pl-10 sm:pl-16 lg:mx-0">
            <div className={cn("overflow-hidden rounded-3xl", softShadow)}>
              <Image
                src="/site/foto/barista.webp"
                alt="Kafede kahve makinesinin yanında durup telefonuna bakan çalışan."
                width={2400}
                height={2041}
                // Önyükleme yok: telefonda LCP başlık; önyükleme yazı tipiyle bant genişliği yarıştırıyordu (Lighthouse, 2026-10-21).
                loading="eager"
                quality={85}
                sizes="(min-width: 640px) 496px, calc(100vw - 72px)"
                className="block aspect-[4/3.4] h-auto w-full object-cover"
              />
            </div>
            <PhoneFrame
              caption={false}
              className="absolute bottom-0 left-0 w-[118px] sm:w-[170px]"
              src="/site/bugun-telefon.webp"
              alt="KobiFabrika telefonda: Bugün ekranında hızlı işlem düğmeleri ve günün randevuları."
            />
          </div>
        </div>
      </section>

      {/* 2) Kimler için: sektör fotoğrafları (sadece atmosfer) */}
      <section aria-labelledby="kimler">
        <div className={cn(container, "py-20 sm:py-24")}>
          <SectionHeading
            id="kimler"
            label="Kimler için"
            title="Hangi işi yapıyorsan ona göre açılır."
            text="Kurulumda sektörünü seçersin; menü, hizmet listesi ve ekranlar ona göre gelir."
          />
          <ul className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {sectors.map((s) => (
              <li key={s.title} className="flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white">
                <Image
                  src={s.photo}
                  alt={s.alt}
                  width={1600}
                  height={1200}
                  quality={85}
                  sizes="(min-width: 1024px) 360px, (min-width: 640px) calc(50vw - 34px), calc(100vw - 32px)"
                  className="aspect-[4/3] h-auto w-full object-cover"
                />
                <div className="p-5">
                  <h3 className="text-lg font-semibold">{s.title}</h3>
                  <p className="mt-1 text-[15px] leading-6 text-slate">{s.text}</p>
                </div>
              </li>
            ))}
            <li className="flex flex-col justify-between gap-6 rounded-2xl bg-cream p-6">
              <div>
                <h3 className="text-lg font-semibold">İşin listede yok mu?</h3>
                <p className="mt-1 text-[15px] leading-6 text-slate">
                  Terzi, oto yıkama, dershane, veteriner… Satışı, müşterisi ve personeli olan her işletme için çalışır.
                </p>
              </div>
              <Link
                href={primaryCta.href}
                className="inline-flex min-h-11 items-center gap-2 font-semibold text-coral-text underline-offset-4 hover:underline"
              >
                Ücretsiz dene <ArrowRight aria-hidden className="size-4" />
              </Link>
            </li>
          </ul>
        </div>
      </section>

      {/* 3) Modüller: Para · Müşteri · Ekip (kısa hali; ayrıntısı /ozellikler) */}
      <section aria-labelledby="ozellikler-ozet" className="border-y border-slate-200 bg-white">
        <div className={cn(container, "py-20 sm:py-24")}>
          <SectionHeading
            id="ozellikler-ozet"
            label="Özellikler"
            title="Defter, ajanda ve hesap makinesi tek yerde."
            text="Paranı takip eder, müşteri getirir, ekibini yönetir. Üç modül, tek hesap."
          />
          <div className="mt-10">
            <FeatureTabs groups={featureGroups} />
          </div>
          <Link
            href="/ozellikler"
            className="mt-10 inline-flex min-h-11 items-center gap-2 font-semibold text-link underline-offset-4 hover:underline"
          >
            Bütün özellikler <ArrowRight aria-hidden className="size-4" />
          </Link>
        </div>
      </section>

      {/* 4) Telefondan yönet: sıcak açık bant */}
      <section aria-labelledby="telefondan" className="bg-cream">
        <div className={cn(container, "grid items-center gap-12 py-20 sm:py-24 lg:grid-cols-2")}>
          <div className="relative mx-auto w-full max-w-[440px] pr-12 pb-8 lg:mx-0">
            <Image
              src="/site/foto/kuafor.webp"
              alt="Aydınlık bir kuaför salonunda müşterisinin saçını boyayan kuaför."
              width={1600}
              height={2000}
              quality={85}
              sizes="(min-width: 640px) 392px, calc(100vw - 80px)"
              className={cn("aspect-[4/5] h-auto w-full rounded-3xl object-cover", softShadow)}
            />
            <PhoneFrame
              caption={false}
              className="absolute right-0 bottom-0 w-[120px] sm:w-[160px]"
              src="/site/online-randevu-telefon.webp"
              alt="Müşterinin telefonunda işletmenin online randevu sayfası: hizmet seçimi."
            />
          </div>
          <div>
            <SectionHeading id="telefondan" label="Telefondan yönet" title="Dükkân seninle cebinde." />
            <ul className="mt-8 flex flex-col gap-6">
              {[
                {
                  icon: Link2,
                  title: "Müşterin randevusunu kendisi alır",
                  text: "Randevu linkini Instagram profiline ve Google işletme kaydına koy; talep gelince onaylarsın.",
                },
                {
                  icon: MessageCircle,
                  title: "WhatsApp'tan tek dokunuşla hatırlat",
                  text: "Yarının randevuları ve vadesi gelen borçlar hazır mesajla; kopyala-yapıştır yok.",
                },
                {
                  icon: Smartphone,
                  title: "Uygulama indirmeden",
                  text: "Tarayıcıdan açılır, ana ekrana eklersin. Ekibin mesaiye telefondan başlar.",
                },
              ].map(({ icon: Icon, title, text }) => (
                <li key={title} className="flex gap-4">
                  <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-white text-ink">
                    <Icon aria-hidden className="size-5" />
                  </span>
                  <div>
                    <h3 className="text-lg font-semibold">{title}</h3>
                    <p className="mt-1 text-base leading-7 text-slate">{text}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </section>

      {/* 5) Farkımız */}
      <section aria-labelledby="farkimiz">
        <div className={cn(container, "py-20 sm:py-24")}>
          <SectionHeading id="farkimiz" label="Farkımız" title="Esnafın derdini dinleyerek yaptık." />
          <ol className="mt-12 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {differentiators.map((d, i) => (
              <li key={d.title} className="rounded-2xl border border-slate-200 bg-white p-6">
                <p className="font-heading text-sm font-semibold text-coral-text tabular-nums">
                  {String(i + 1).padStart(2, "0")}
                </p>
                <h3 className="mt-2 text-lg font-semibold">{d.title}</h3>
                <p className="mt-1 text-[15px] leading-6 text-slate">{d.text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* 6) Nasıl çalışır */}
      <section aria-labelledby="nasil" className="border-y border-slate-200 bg-mist">
        <div className={cn(container, "py-20 sm:py-24")}>
          <SectionHeading id="nasil" label="Nasıl çalışır" title="Üç adımda başla." />
          <ol className="mt-12 grid gap-5 lg:grid-cols-3">
            {steps.map((s, i) => (
              <li key={s.title} className="rounded-2xl bg-white p-6">
                <p className="flex size-11 items-center justify-center rounded-full bg-brand font-heading text-lg font-semibold text-ink tabular-nums">
                  {i + 1}
                </p>
                <h3 className="mt-4 text-xl font-semibold">{s.title}</h3>
                <p className="mt-2 text-base leading-7 text-slate">{s.text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* 7) Fiyatlar */}
      <section aria-labelledby="fiyatlar">
        <div className={cn(container, "py-20 sm:py-24")}>
          <SectionHeading
            id="fiyatlar"
            label="Fiyatlar"
            title="Ücretsiz başla, işin büyüyünce geç."
            text="TL, sabit ve komisyonsuz fiyat. Kalfa ve Usta'yı 14 gün ücretsiz dene."
          />
          <div className="mt-10">
            <Pricing />
          </div>
        </div>
      </section>

      {/* 8) SSS */}
      <section aria-labelledby="sss" className="border-t border-slate-200">
        <div className={cn(container, "grid gap-10 py-20 sm:py-24 lg:grid-cols-[1fr_1.6fr]")}>
          <div>
            <SectionHeading id="sss" label="SSS" title="Sık sorulan sorular" />
            <Link
              href="/sss"
              className="mt-6 inline-flex min-h-11 items-center font-semibold text-link underline underline-offset-4"
            >
              Bütün sorular
            </Link>
          </div>
          <FaqList limit={5} />
        </div>
      </section>

      {/* 9) Son çağrı: mürekkep bant + fotoğraf */}
      <section aria-labelledby="son-cagri" className={cn(container, "pb-20 sm:pb-24")}>
        <div className="grid overflow-hidden rounded-3xl bg-ink text-white lg:grid-cols-[1.2fr_1fr]">
          <div className="p-8 sm:p-12">
            <h2
              id="son-cagri"
              className="max-w-xl text-4xl leading-[1.05] font-semibold tracking-[-0.03em] sm:text-5xl"
            >
              Defteri bugün kapat.
            </h2>
            <p className="mt-5 max-w-md text-lg text-slate-300">
              10 dakikada kur, Çırak paketiyle ücretsiz kullan. Kredi kartı istemez.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-6">
              <Button asChild size="lg" className="h-12 px-7 text-base text-ink">
                <Link href={primaryCta.href}>
                  {primaryCta.label} <ArrowRight aria-hidden data-icon="inline-end" />
                </Link>
              </Button>
              <Link
                href="/iletisim"
                className="inline-flex min-h-11 items-center text-base font-semibold text-brand underline underline-offset-4 hover:no-underline"
              >
                Soru sor
              </Link>
            </div>
          </div>
          <Image
            src="/site/foto/servis.webp"
            alt="Aydınlık bir kafede müşterisiyle sohbet ederek servis yapan çalışan."
            width={2400}
            height={1600}
            quality={85}
            sizes="(min-width: 1024px) 500px, calc(100vw - 32px)"
            className="h-full min-h-64 w-full object-cover"
          />
        </div>
      </section>
    </>
  );
}
