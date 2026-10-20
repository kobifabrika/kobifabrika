import type { ReactNode } from "react";

/** Alt sayfaların koyu lacivert başlık bandı (ana sayfanın girişiyle aynı dil). */
export function PageIntro({ label, title, children }: { label: string; title: string; children?: ReactNode }) {
  return (
    <section className="dark bg-navy-950 text-white">
      <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-16">
        <p className="text-sm font-semibold text-cyan">{label}</p>
        <h1 className="mt-3 max-w-3xl text-4xl leading-[1.05] font-bold tracking-tight sm:text-5xl">{title}</h1>
        {children && <div className="mt-5 max-w-2xl text-lg text-neutral-300">{children}</div>}
      </div>
    </section>
  );
}
