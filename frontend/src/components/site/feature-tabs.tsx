"use client";

import { cn } from "cn";
import { Check } from "lucide-react";
import { useState } from "react";

import { BrowserFrame } from "@/components/site/device-frames";
import type { FeatureGroup } from "@/content/site";

/**
 * Modül bölümü (Paraşüt tarzı): Para · Müşteri · Ekip sekmeleri; solda anlatım ve maddeler, sağda çerçevede gerçek ekran.
 * Bütün paneller sayfada (arama motoru ve JavaScript'siz okuma için); seçili olmayanlar `hidden`.
 */
export function FeatureTabs({ groups }: { groups: FeatureGroup[] }) {
  const [active, setActive] = useState(groups[0]!.key);
  return (
    <div>
      <div role="tablist" aria-label="Modüller" className="flex gap-2 overflow-x-auto pb-1">
        {groups.map((g) => (
          <button
            key={g.key}
            id={`modul-${g.key}`}
            type="button"
            role="tab"
            aria-selected={active === g.key}
            aria-controls={`modul-panel-${g.key}`}
            onClick={() => setActive(g.key)}
            className={cn(
              "min-h-11 shrink-0 rounded-full border px-5 text-[15px] font-semibold transition-colors",
              active === g.key ? "border-navy-950 bg-navy-950 text-white" : "border-neutral-200 bg-white text-navy-950 hover:border-navy-950",
            )}
          >
            {g.label}
          </button>
        ))}
      </div>
      {groups.map((g) => (
        <div
          key={g.key}
          id={`modul-panel-${g.key}`}
          role="tabpanel"
          aria-labelledby={`modul-${g.key}`}
          hidden={active !== g.key}
          className="mt-8 grid items-center gap-10 lg:grid-cols-[1fr_1.35fr] lg:gap-14"
        >
          <div>
            <h3 className="text-3xl leading-tight font-semibold tracking-tight sm:text-4xl">{g.title}</h3>
            <p className="mt-4 max-w-md text-base leading-7 text-neutral-600">{g.text}</p>
            <ul className="mt-6 flex flex-col gap-3">
              {g.points.map((p) => (
                <li key={p} className="flex items-center gap-3 text-base">
                  <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-coral-soft">
                    <Check aria-hidden className="size-3.5 text-navy-950" strokeWidth={3} />
                  </span>
                  {p}
                </li>
              ))}
            </ul>
          </div>
          <div className="rounded-2xl bg-neutral-100 p-3 sm:p-6">
            <BrowserFrame src={g.shot} alt={g.shotAlt} />
          </div>
        </div>
      ))}
    </div>
  );
}
