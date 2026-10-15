"use client";

import { CalendarClock, Megaphone, UserMinus } from "lucide-react";
import Link from "next/link";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { DashboardMarketing, EmptySlot } from "@/lib/api/types";
import { formatDay } from "@/lib/format";

/** Boş aralıktan kampanya sihirbazına: hazır mesajla, tüm müşteriler hedefli. */
export function emptySlotCampaignHref(date: string, slot: Pick<EmptySlot, "start" | "end" | "staffName">) {
  const q = new URLSearchParams({ tur: "bos-saat", tarih: date, saat: slot.start, bitis: slot.end, personel: slot.staffName });
  return `/panel/kampanyalar/yeni?${q}`;
}

export const riskCampaignHref = "/panel/kampanyalar/yeni?segment=at-risk&tur=risk";

/** Bugün ekranı: yarın boş saatler ve kaybedilme riski (boşsa kart gösterilmez; Sahip ve Yönetici). */
export function MarketingCards({ data }: { data: DashboardMarketing }) {
  const slots = data.emptySlots.items;
  const risk = data.atRisk;
  if (slots.length === 0 && risk.count === 0) return null;
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      {slots.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-black">
              <CalendarClock aria-hidden className="size-5" /> Yarın boş saatler
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <p className="text-sm text-neutral-700">{formatDay(data.emptySlots.date)} için müşterilerinize haber verin.</p>
            <ul className="flex flex-col divide-y divide-border" aria-label="Yarın boş saatler">
              {slots.map((s) => (
                <li key={`${s.staffId}-${s.start}`} className="flex items-center justify-between gap-3 py-2">
                  <span className="min-w-0 text-sm text-black">
                    <span className="font-semibold tabular-nums">
                      {s.start}-{s.end}
                    </span>{" "}
                    <span className="break-words">{s.staffName} boş</span>
                  </span>
                  <Button asChild variant="outline" className="shrink-0">
                    <Link href={emptySlotCampaignHref(data.emptySlots.date, s)} aria-label={`${s.start}-${s.end} ${s.staffName}: kampanya başlat`}>
                      <Megaphone aria-hidden /> Kampanya
                    </Link>
                  </Button>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}
      {risk.count > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-black">
              <UserMinus aria-hidden className="size-5" /> Kaybedilme riski
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <p className="text-sm text-neutral-700">
              {risk.count} düzenli müşteri her zamankinden uzun süredir gelmedi.
            </p>
            <ul className="flex flex-col divide-y divide-border" aria-label="Kaybedilme riskindeki müşteriler">
              {risk.items.map((c) => (
                <li key={c.id} className="flex items-center justify-between gap-3 py-2">
                  <Link href={`/panel/musteriler/${c.id}`} className="min-w-0 truncate text-sm font-semibold text-black underline-offset-4 hover:underline">
                    {c.name}
                  </Link>
                  <span className="shrink-0 text-xs text-neutral-600 tabular-nums">
                    {c.daysSince} gündür · her ~{Math.round(c.avgIntervalDays)} günde gelir
                  </span>
                </li>
              ))}
            </ul>
            <div className="grid grid-cols-2 gap-2 sm:flex">
              <Button asChild variant="outline">
                <Link href="/panel/musteriler?segment=at-risk">Listeyi gör</Link>
              </Button>
              <Button asChild>
                <Link href={riskCampaignHref}>
                  <Megaphone aria-hidden /> Kampanya başlat
                </Link>
              </Button>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
