import { ChevronLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { PhoneFrame } from "@/components/site/device-frames";
import { findArticle, helpArticles } from "@/content/help";

export function generateStaticParams() {
  return helpArticles.map((a) => ({ makale: a.slug }));
}

export async function generateMetadata({ params }: PageProps<"/yardim/[makale]">): Promise<Metadata> {
  const article = findArticle((await params).makale);
  if (!article) return { title: "Yardım" };
  return { title: article.title, description: article.summary, alternates: { canonical: `/yardim/${article.slug}` } };
}

export default async function HelpArticlePage({ params }: PageProps<"/yardim/[makale]">) {
  const article = findArticle((await params).makale);
  if (!article) notFound();
  return (
    <article className="mx-auto max-w-5xl px-4 py-10 sm:px-6 sm:py-14">
      <Link href="/yardim" className="flex min-h-11 w-fit items-center gap-1 text-sm font-semibold text-link hover:underline">
        <ChevronLeft aria-hidden className="size-4" />
        Yardım
      </Link>
      <h1 className="mt-4 max-w-3xl text-3xl leading-tight font-bold tracking-tight sm:text-4xl">{article.title}</h1>
      <p className="mt-3 text-lg text-slate">
        {article.summary} <span className="text-sm text-slate">· {article.minutes} dakikalık okuma</span>
      </p>
      <div className="mt-10 grid gap-10 md:grid-cols-[1fr_auto]">
        <ol className="flex flex-col gap-6">
          {article.steps.map((s, i) => (
            <li key={s.title} className="flex gap-4">
              <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-brand font-semibold text-ink tabular-nums">{i + 1}</span>
              <div>
                <h2 className="text-lg font-semibold">{s.title}</h2>
                <p className="mt-1 text-base leading-7 text-slate">{s.text}</p>
              </div>
            </li>
          ))}
        </ol>
        <PhoneFrame className="mx-auto md:mx-0" src={article.image.src} alt={article.image.alt} />
      </div>
      {article.tips && (
        <aside aria-label="İpuçları" className="mt-10 border-l-2 border-brand bg-mist px-4 py-3">
          <p className="text-sm font-semibold text-ink">İpuçları</p>
          <ul className="mt-2 flex list-disc flex-col gap-1 pl-5 text-sm text-slate">
            {article.tips.map((t) => (
              <li key={t}>{t}</li>
            ))}
          </ul>
        </aside>
      )}
      <p className="mt-10 text-base text-slate">
        Olmadı mı?{" "}
        <Link href="/iletisim" className="font-semibold text-link underline underline-offset-4">
          Bize yaz
        </Link>
        , birlikte bakalım.
      </p>
    </article>
  );
}
