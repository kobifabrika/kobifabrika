"use client";

import { ChevronRight, Search } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

import { Input } from "@/components/ui/input";
import { helpArticles } from "@/content/help";

const fold = (s: string) => s.toLocaleLowerCase("tr-TR");

/** Makale listesi ve istemcide arama (başlık, özet ve adımlarda). */
export function HelpList() {
  const [term, setTerm] = useState("");
  const q = fold(term.trim());
  const list = q
    ? helpArticles.filter((a) => fold([a.title, a.summary, ...a.steps.map((s) => `${s.title} ${s.text}`)].join(" ")).includes(q))
    : helpArticles;
  return (
    <div className="flex flex-col gap-6">
      <div className="relative">
        <Search aria-hidden className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-slate" />
        <Input type="search" aria-label="Yardımda ara" placeholder="Ara: randevu, WhatsApp, kurulum…" className="pl-9" value={term} onChange={(e) => setTerm(e.target.value)} />
      </div>
      {list.length === 0 ? (
        <p className="text-base text-slate">Sonuç yok. Başka bir kelime dene ya da bize yaz.</p>
      ) : (
        <ul className="flex flex-col divide-y divide-slate-200 border-y border-slate-200">
          {list.map((a) => (
            <li key={a.slug}>
              <Link href={`/yardim/${a.slug}`} className="flex min-h-16 items-center gap-4 py-4 hover:bg-mist">
                <span className="flex min-w-0 flex-1 flex-col gap-1">
                  <span className="text-lg font-semibold text-ink">{a.title}</span>
                  <span className="text-sm text-slate">
                    {a.summary} · {a.minutes} dk
                  </span>
                </span>
                <ChevronRight aria-hidden className="size-5 shrink-0 text-link" />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
