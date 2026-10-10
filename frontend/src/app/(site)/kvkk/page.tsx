import type { Metadata } from "next";

import { kvkkSections, kvkkTitle } from "@/content/kvkk-aydinlatma";

export const metadata: Metadata = { title: "KVKK aydınlatma metni" };

export default function KvkkPage() {
  return (
    <article className="mx-auto max-w-3xl px-4 sm:px-6 py-12 sm:py-16">
      <h1 className="text-4xl font-bold tracking-tight text-black">{kvkkTitle}</h1>
      <div className="mt-8 flex flex-col gap-6 text-base leading-7 text-neutral-700">
        {kvkkSections.map((s) => (
          <section key={s.heading}>
            <h2 className="text-lg font-semibold text-black">{s.heading}</h2>
            <p className="mt-1">{s.body}</p>
          </section>
        ))}
      </div>
    </article>
  );
}
