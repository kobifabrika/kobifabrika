import type { Metadata } from "next";
import Link from "next/link";

import { PageIntro } from "@/components/site/page-intro";
import { Button } from "@/components/ui/button";
import { company } from "@/content/company";
import { primaryCta } from "@/content/site";

export const metadata: Metadata = {
  title: "Hakkımızda",
  description: "KobiFabrika, esnafın parasını takip eden, müşteri getiren ve ekibini yöneten tek uygulamayı yapıyor.",
  alternates: { canonical: "/hakkimizda" },
};

const principles = [
  { title: "Esnafın dili", text: "Ekranlarda muhasebe jargonu değil, dükkânda konuşulan dil: veresiye, kasa, randevu." },
  { title: "Önce telefon", text: "Esnaf gün boyu ayakta; her ekran önce telefonda denenir, sonra bilgisayarda." },
  { title: "Dürüst fiyat", text: "TL, sabit, komisyonsuz. Ücretsiz paket gerçekten ücretsiz; sürpriz zam yok." },
  { title: "Veri senin", text: "Verin satılmaz, reklam için kullanılmaz; istediğin an Excel'e aktarırsın." },
];

export default function AboutPage() {
  return (
    <>
      <PageIntro label="Hakkımızda" title="Esnafın derdini dinleyerek yapıyoruz.">
        KobiFabrika; kuaförün, kafenin, stüdyonun ve dükkânın günlük işini tek uygulamada toplamak için kuruldu.
      </PageIntro>
      <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6 sm:py-16">
        <div className="flex flex-col gap-5 text-lg leading-8 text-neutral-700">
          <p>
            Küçük işletmeler bugün iki ayrı program kullanıyor: biri parayı tutuyor, öbürü randevuyu. Müşteri iki ayrı listede, borç
            defterde, hatırlatma telefonun notlarında. Biz bunları tek yere topluyoruz.
          </p>
          <p>
            Şu anda kuaför, pilates stüdyosu ve kafe-restoranlarla pilot dönemindeyiz. Her hafta onların söylediklerini ekranlara
            taşıyoruz.
          </p>
        </div>
        <h2 className="mt-12 text-2xl font-bold">Nasıl çalışıyoruz</h2>
        <ul className="mt-6 grid gap-4 sm:grid-cols-2">
          {principles.map((p) => (
            <li key={p.title} className="rounded-lg border-t-4 border-t-link bg-neutral-50 p-5">
              <h3 className="text-lg font-semibold">{p.title}</h3>
              <p className="mt-1 text-base leading-7 text-neutral-700">{p.text}</p>
            </li>
          ))}
        </ul>
        <p className="mt-12 text-sm text-neutral-600">{company.name}</p>
        <Button asChild size="lg" className="mt-6">
          <Link href={primaryCta.href}>{primaryCta.label}</Link>
        </Button>
      </div>
    </>
  );
}
