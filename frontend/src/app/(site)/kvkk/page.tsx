import type { Metadata } from "next";

import { kvkkSections, kvkkTitle } from "@/content/kvkk-aydinlatma";

export const metadata: Metadata = { title: "KVKK aydınlatma metni" };

export default function KvkkPage() {
  return (
    <article className="mx-auto max-w-3xl px-4 py-12 sm:py-16">
      <h1 className="text-3xl font-bold tracking-tight text-white">{kvkkTitle}</h1>
      <div className="mt-8 flex flex-col gap-6 text-base leading-7 text-neutral-300">
        {kvkkSections.map((s) => (
          <section key={s.heading}>
            <h2 className="text-lg font-semibold text-white">{s.heading}</h2>
            <p className="mt-1">{s.body}</p>
          </section>
        ))}
      </div>
    </article>
  );
}
