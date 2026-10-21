import { Plus } from "lucide-react";

import { faqs } from "@/content/site";

/** Sık sorulan sorular: açılır kapanır (details/summary; JavaScript gerekmez). */
export function FaqList({ limit }: { limit?: number }) {
  return (
    <div className="border-b border-slate-200">
      {faqs.slice(0, limit).map(({ q, a }) => (
        <details key={q} className="group border-t border-slate-200">
          <summary className="flex min-h-16 cursor-pointer list-none items-center justify-between gap-4 py-4 text-base font-semibold sm:text-lg [&::-webkit-details-marker]:hidden">
            {q}
            <Plus aria-hidden className="size-5 shrink-0 text-link transition-transform group-open:rotate-45 motion-reduce:transition-none" />
          </summary>
          <p className="max-w-2xl pb-6 text-base leading-7 text-slate">{a}</p>
        </details>
      ))}
    </div>
  );
}

/** FAQPage yapılandırılmış verisi (arama motorları). */
export function faqJsonLd() {
  return {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: faqs.map((f) => ({ "@type": "Question", name: f.q, acceptedAnswer: { "@type": "Answer", text: f.a } })),
  };
}
