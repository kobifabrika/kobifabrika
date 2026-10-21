import type { Metadata } from "next";

import { FaqList } from "@/components/site/faq-list";
import { PageIntro } from "@/components/site/page-intro";
import { Pricing } from "@/components/site/pricing";

export const metadata: Metadata = {
  title: "Fiyatlar",
  description: "Çırak ücretsiz; Kalfa 699 TL/ay, Usta 1.299 TL/ay (KDV hariç). Yıllıkta 2 ay hediye, 14 gün ücretsiz deneme.",
  alternates: { canonical: "/fiyatlar" },
};

export default function PricingPage() {
  return (
    <>
      <PageIntro label="Fiyatlar" title="Ücretsiz başla, işin büyüyünce geç." compact>
        TL, sabit ve komisyonsuz. Kalfa ve Usta&apos;yı 14 gün bütün özellikleriyle ücretsiz dene; kredi kartı istemez.
      </PageIntro>
      <div className="mx-auto max-w-6xl px-4 py-6 sm:px-6 sm:py-8">
        <h2 className="sr-only">Paketler</h2>
        <Pricing />
        <h2 className="mt-16 text-2xl font-bold">Sık sorulanlar</h2>
        <div className="mt-4">
          <FaqList limit={4} />
        </div>
      </div>
    </>
  );
}
