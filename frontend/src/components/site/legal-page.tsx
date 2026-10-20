import type { Metadata } from "next";

import { companyLine } from "@/content/company";
import type { LegalDoc, LegalSection } from "@/content/legal";

export function legalMetadata(doc: LegalDoc): Metadata {
  return { title: doc.short, description: doc.description, alternates: { canonical: `/${doc.slug}` } };
}

/** Hukuki metin bölümleri (site sayfaları, kayıt penceresi ve /r/[slug]/aydinlatma ortak). */
export function LegalSections({ sections }: { sections: LegalSection[] }) {
  return (
    <div className="flex flex-col gap-6 text-base leading-7 text-neutral-700">
      {sections.map((s) => (
        <section key={s.heading}>
          <h2 className="text-lg font-semibold text-black">{s.heading}</h2>
          {s.paragraphs?.map((p) => (
            <p key={p} className="mt-1">
              {p}
            </p>
          ))}
          {s.items && (
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {s.items.map((i) => (
                <li key={i}>{i}</li>
              ))}
            </ul>
          )}
        </section>
      ))}
    </div>
  );
}

/** Hukuki metin sayfası: başlık, sürüm ve son güncelleme, giriş, bölümler, şirket künyesi. */
export function LegalPage({ doc }: { doc: LegalDoc }) {
  return (
    <article className="mx-auto max-w-3xl px-4 py-12 sm:px-6 sm:py-16">
      <h1 className="text-3xl font-bold tracking-tight text-black sm:text-4xl">{doc.title}</h1>
      <p className="mt-3 text-sm text-neutral-600">
        Son güncelleme: {doc.updated} · Sürüm {doc.version}
      </p>
      {doc.intro && <p className="mt-6 text-base leading-7 text-neutral-700">{doc.intro}</p>}
      <div className="mt-8">
        <LegalSections sections={doc.sections} />
      </div>
      <p className="mt-10 border-t border-neutral-200 pt-4 text-sm text-neutral-600">{companyLine()}</p>
    </article>
  );
}
