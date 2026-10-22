"use client";

import { cn } from "cn";
import { Check } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

import { softShadow } from "@/components/site/device-frames";
import { Button } from "@/components/ui/button";
import {
  allPlansInclude,
  formatPlanPrice,
  planCta,
  planFootnote,
  planLimits,
  planPriceNote,
  plans,
  signupHref,
  type Period,
} from "@/content/plans";

/**
 * Paket kartları: Aylık/Yıllık anahtarı (yıllıkta 2 ay hediye), kayıt linki (?paket=&donem=), limitler, özellikler.
 * Düğme fiyatın hemen altında, özellikler ondan sonra: Fiyatlar sayfasında düğmeler ilk ekranda görünür (2026-10-21).
 */
export function Pricing() {
  const [period, setPeriod] = useState<Period>("aylik");
  return (
    <div className="flex flex-col gap-8">
      <div role="radiogroup" aria-label="Ödeme dönemi" className="inline-flex w-fit rounded-full border border-neutral-200 bg-white p-1">
        {(["aylik", "yillik"] as const).map((p) => (
          <button
            key={p}
            type="button"
            role="radio"
            aria-checked={period === p}
            onClick={() => setPeriod(p)}
            className={cn(
              "min-h-11 rounded-full px-5 text-sm font-semibold",
              period === p ? "bg-navy-950 text-white" : "text-navy-950 hover:bg-neutral-100",
            )}
          >
            {p === "aylik" ? "Aylık" : "Yıllık · 2 ay hediye"}
          </button>
        ))}
      </div>
      <ul className="grid gap-4 lg:grid-cols-3">
        {plans.map((plan) => (
          <li
            key={plan.id}
            className={cn(
              "flex flex-col rounded-2xl border bg-white p-6",
              plan.highlighted ? cn("border-2 border-brand", softShadow) : "border-neutral-200",
            )}
          >
            <div className="flex items-baseline justify-between gap-3">
              <h3 className="text-2xl font-bold">{plan.name}</h3>
              {plan.highlighted && <span className="rounded-full bg-coral-soft px-2.5 py-0.5 text-xs font-semibold text-navy-950">Önerilen</span>}
            </div>
            <p className="mt-1 text-sm text-neutral-600">{plan.tagline}</p>
            <p className="mt-6 font-heading text-4xl font-semibold tracking-tight">
              <span>{formatPlanPrice(plan, period)}</span>
              {plan.monthlyPrice > 0 && <span className="text-base font-medium text-neutral-600"> / ay</span>}
            </p>
            <p className="mt-1 text-sm text-neutral-600">{planPriceNote(plan, period)}</p>
            <Button asChild size="lg" variant={plan.highlighted ? "default" : "outline"} className="mt-5 w-full">
              <Link href={signupHref(plan, period)}>{planCta(plan)}</Link>
            </Button>
            <p className="mt-6 border-t border-neutral-200 pt-5 text-sm font-semibold text-navy-950">{planLimits(plan)}</p>
            <ul className="mt-4 flex flex-col gap-3">
              {plan.features.map((f) => (
                <li key={f} className="flex items-start gap-2.5 text-sm">
                  <Check aria-hidden className="mt-0.5 size-4 shrink-0 text-link" strokeWidth={2.5} /> {f}
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
      <div className="grid gap-4 rounded-2xl bg-neutral-100 p-6 lg:grid-cols-[1fr_2fr]">
        <p className="font-semibold">Bütün paketlerde</p>
        <div>
          <ul className="grid gap-2 sm:grid-cols-2">
            {allPlansInclude.map((t) => (
              <li key={t} className="flex items-start gap-2.5 text-sm">
                <Check aria-hidden className="mt-0.5 size-4 shrink-0 text-link" strokeWidth={2.5} /> {t}
              </li>
            ))}
          </ul>
          <p className="mt-4 text-sm text-neutral-600">{planFootnote}</p>
        </div>
      </div>
    </div>
  );
}
