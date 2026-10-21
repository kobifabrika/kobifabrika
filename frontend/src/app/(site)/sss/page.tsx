import type { Metadata } from "next";
import Link from "next/link";

import { FaqList, faqJsonLd } from "@/components/site/faq-list";
import { PageIntro } from "@/components/site/page-intro";

export const metadata: Metadata = {
  title: "Sık sorulan sorular",
  description: "KobiFabrika hakkında sık sorulan sorular: ücretsiz paket, pilot dönemi, telefonda kullanım, veri güvenliği, destek.",
  alternates: { canonical: "/sss" },
};

export default function FaqPage() {
  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(faqJsonLd()) }} />
      <PageIntro label="SSS" title="Sık sorulan sorular" />
      <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6 sm:py-16">
        <FaqList />
        <p className="mt-8 text-base text-neutral-600">
          Cevabını bulamadın mı?{" "}
          <Link href="/iletisim" className="font-semibold text-link underline underline-offset-4">
            Bize yaz
          </Link>{" "}
          ya da{" "}
          <Link href="/yardim" className="font-semibold text-link underline underline-offset-4">
            yardım makalelerine
          </Link>{" "}
          bak.
        </p>
      </div>
    </>
  );
}
