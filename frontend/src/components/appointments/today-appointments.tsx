"use client";

import { CalendarDays, ChevronRight } from "lucide-react";
import Link from "next/link";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { DashboardAppointments } from "@/lib/api/types";
import { istanbulParts } from "@/lib/format";

import { StatusBadge } from "./appointment-bits";

/**
 * Bugün ekranı → "Bugünün randevuları" (Randevular modülü açıkken her zaman görünür; boşsa "Randevu ver").
 * Personel rolü kendi randevularını görür.
 */
export function TodayAppointments({ data, canWrite }: { data: DashboardAppointments; canWrite: boolean }) {
  return (
    <Card className="gap-3 border-t-4 border-t-brand py-4">
      <CardHeader className="flex flex-row items-center justify-between gap-2 px-4">
        <CardTitle className="flex items-center gap-2 text-base font-semibold text-black">
          <CalendarDays aria-hidden className="size-5 text-link" />
          {data.onlyOwn ? "Bugünkü randevularım" : "Bugünün randevuları"}
          <span className="text-sm font-normal text-neutral-600 tabular-nums">({data.total})</span>
        </CardTitle>
        {data.pending > 0 && <span className="text-sm text-neutral-700">Onay bekleyen {data.pending}</span>}
      </CardHeader>
      <CardContent className="flex flex-col gap-2 px-4">
        {data.items.length === 0 ? (
          <p className="flex flex-wrap items-center gap-x-3 text-sm text-neutral-600">
            Bugün randevu yok.
            {canWrite && (
              <Link href="/panel/randevular?yeni=1" className="inline-flex min-h-11 items-center font-semibold text-black underline">
                Randevu ver
              </Link>
            )}
          </p>
        ) : (
          <ul className="flex flex-col divide-y divide-border">
            {data.items.map((a) => (
              <li key={a.id}>
                <Link href={`/panel/randevular?randevu=${a.id}`} className="flex min-h-12 items-center gap-3 py-1 hover:bg-neutral-100">
                  <span className="w-12 text-sm font-semibold text-black tabular-nums">{istanbulParts(a.startAt).time}</span>
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="truncate text-sm font-semibold text-black">{a.contactName}</span>
                    <span className="truncate text-xs text-neutral-600">
                      {a.serviceName}
                      {data.onlyOwn ? "" : ` · ${a.staffName}`}
                    </span>
                  </span>
                  <StatusBadge status={a.status} />
                </Link>
              </li>
            ))}
          </ul>
        )}
        {data.total > 0 && (
          <Link href="/panel/randevular" className="inline-flex min-h-11 items-center gap-1 self-start text-sm font-semibold text-black underline-offset-4 hover:underline">
            Takvimde gör
            <ChevronRight aria-hidden className="size-4" />
          </Link>
        )}
      </CardContent>
    </Card>
  );
}
