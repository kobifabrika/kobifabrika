"use client";

import { ChevronRight, Clock, UsersRound } from "lucide-react";
import Link from "next/link";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { halfDayNames, type DashboardStaff } from "@/lib/api/types";
import { istanbulParts } from "@/lib/format";

import { MyClock } from "./my-clock";

/**
 * Bugün → "Aktif personel" (Sahip, Yönetici, Mali Müşavir; personel kaydı yoksa API null döner, kart gösterilmez): mesaidekiler
 * ("09:02'den beri"), bugün izinliler (tür dahil) ve çıkışı unutulan kayıtlar için Mesai ekranına link.
 */
export function TodayStaff({ data }: { data: DashboardStaff }) {
  const forgottenHref = `/panel/mesai-izin?sekme=mesai${data.forgottenDay ? `&tarih=${data.forgottenDay}` : ""}`;
  return (
    <Card className="gap-3 py-4">
      <CardHeader className="flex flex-row items-center justify-between gap-2 px-4">
        <CardTitle className="flex items-center gap-2 text-base font-semibold text-black">
          <UsersRound aria-hidden className="size-5 text-neutral-600" />
          Aktif personel
          <span className="text-sm font-normal text-neutral-600 tabular-nums">({data.onClock.length} mesaide)</span>
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 px-4">
        {data.onClock.length === 0 ? (
          <p className="text-sm text-neutral-600">Şu an mesaide kimse yok.</p>
        ) : (
          <ul aria-label="Mesaidekiler" className="flex flex-col divide-y divide-border">
            {data.onClock.map((o) => (
              <li key={o.staffMemberId} className="flex min-h-11 items-center justify-between gap-3 py-1">
                <span className="truncate text-sm font-semibold text-black">{o.name}</span>
                <span className="shrink-0 text-sm text-neutral-700 tabular-nums">{istanbulParts(o.since).time}&apos;den beri</span>
              </li>
            ))}
          </ul>
        )}
        {data.onLeave.length > 0 && (
          <div className="flex flex-col gap-1 border-t border-border pt-3">
            <h3 className="text-xs font-semibold text-neutral-600">Bugün izinli</h3>
            <ul aria-label="Bugün izinli" className="flex flex-col">
              {data.onLeave.map((l) => (
                <li key={l.staffMemberId} className="flex min-h-9 items-center justify-between gap-3 text-sm">
                  <span className="truncate text-black">{l.name}</span>
                  <span className="shrink-0 text-neutral-700">
                    {l.typeName}
                    {l.halfDay !== "None" && ` · ${halfDayNames[l.halfDay].toLocaleLowerCase("tr-TR")}`}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}
        <div className="flex flex-wrap gap-x-4">
          {data.forgottenCount > 0 && (
            <Link href={forgottenHref} className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-black underline">
              {data.forgottenCount} çıkış unutuldu
            </Link>
          )}
          <Link
            href="/panel/mesai-izin"
            className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-black underline-offset-4 hover:underline"
          >
            Mesai ve izin
            <ChevronRight aria-hidden className="size-4" />
          </Link>
        </div>
      </CardContent>
    </Card>
  );
}

/** Bugün → Personel rolünün "Mesaim" kartı: durum ve tek düğme (hesabı personele bağlı değilse API null döner, kart yok). */
export function TodayMyClock() {
  return (
    <Card className="gap-3 py-4">
      <CardHeader className="px-4">
        <CardTitle className="flex items-center gap-2 text-base font-semibold text-black">
          <Clock aria-hidden className="size-5 text-neutral-600" />
          Mesaim
        </CardTitle>
      </CardHeader>
      <CardContent className="px-4">
        <MyClock compact />
      </CardContent>
    </Card>
  );
}
