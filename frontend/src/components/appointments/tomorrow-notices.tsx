"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, MessageCircle } from "lucide-react";
import Link from "next/link";

import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api, unwrap } from "@/lib/api/client";
import type { TomorrowNotice } from "@/lib/api/types";
import { istanbulParts } from "@/lib/format";
import { can } from "@/lib/permissions";

import { appointmentsKey } from "./appointment-bits";
import { recordWhatsApp } from "./whatsapp-notice";

export const tomorrowKey = [...appointmentsKey, "tomorrow"] as const;

/**
 * "Yarının randevuları" (SMS'siz pilot): her satırda WhatsApp hatırlatma linki; dokununca "Hatırlatıldı ✓ 18:42". Liste POST ile
 * gelir (hatırlatmadaki iptal linki token'ı üretilebilir). SMS hatırlatması açıksa (smsReminders) kart gösterilmez.
 * Mali Müşavir listeyi görür; hatırlatma sayacı, açıklama ve düğme sütunu yok (whatsappUrl null).
 */
export function TomorrowNotices() {
  const queryClient = useQueryClient();
  const remind = can(useSession().role, "writeAppointments");
  const query = useQuery({
    queryKey: tomorrowKey,
    queryFn: () => unwrap(api.POST("/api/appointments/tomorrow-notices")),
    staleTime: 60_000,
  });

  if (query.isError) return <QueryError error={query.error} onRetry={() => query.refetch()} />;
  if (!query.data || query.data.smsReminders) return null;
  const { items } = query.data;
  const reminded = items.filter((i) => i.remindedAt).length;

  return (
    <Card role="region" className="gap-3 border-t-4 border-t-link py-4" aria-labelledby="tomorrow-title">
      <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-2 px-4">
        <CardTitle id="tomorrow-title" className="flex items-center gap-2 text-base font-semibold text-black">
          <MessageCircle aria-hidden className="size-5 text-link" />
          Yarının randevuları
          <span className="text-sm font-normal text-neutral-600 tabular-nums">({items.length})</span>
        </CardTitle>
        {remind && items.length > 0 && (
          <span className="text-sm text-neutral-700 tabular-nums">
            Hatırlatılan {reminded}/{items.length}
          </span>
        )}
      </CardHeader>
      <CardContent className="flex flex-col gap-2 px-4">
        {items.length === 0 ? (
          <p className="text-sm text-neutral-600">Yarın için randevu yok.</p>
        ) : (
          <>
            {remind && (
              <p className="text-sm text-neutral-600">
                Müşterilere WhatsApp&apos;tan hatırlatma gönderin; mesaj hazır gelir.
              </p>
            )}
            <ul className="flex flex-col divide-y divide-border">
              {items.map((item) => (
                <NoticeRow
                  key={item.id}
                  item={item}
                  remind={remind}
                  onSent={() => void queryClient.invalidateQueries({ queryKey: tomorrowKey })}
                />
              ))}
            </ul>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function NoticeRow({ item, remind, onSent }: { item: TomorrowNotice; remind: boolean; onSent: () => void }) {
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2">
      <Link
        href={`/panel/randevular?randevu=${item.id}`}
        className="flex min-h-11 min-w-0 flex-1 items-center gap-3 hover:underline"
      >
        <span className="w-12 shrink-0 text-sm font-semibold text-black tabular-nums">
          {istanbulParts(item.startAt).time}
        </span>
        <span className="flex min-w-0 flex-col">
          <span className="truncate text-sm font-semibold text-black">
            {item.contactName}
            {item.status === "Pending" && <span className="font-normal text-neutral-600"> · onay bekliyor</span>}
          </span>
          <span className="truncate text-xs text-neutral-600">
            {item.serviceName} · {item.staffName}
          </span>
        </span>
      </Link>
      {!remind ? null : item.remindedAt ? (
        <span className="flex min-h-11 items-center gap-1 text-sm text-black tabular-nums">
          <Check aria-hidden className="size-4" />
          Hatırlatıldı {istanbulParts(item.remindedAt).time}
        </span>
      ) : item.whatsappUrl ? (
        <Button asChild variant="outline">
          <a
            href={item.whatsappUrl}
            target="_blank"
            rel="noopener noreferrer"
            onClick={() => recordWhatsApp(item.id, "Reminder", onSent)}
          >
            <MessageCircle aria-hidden />
            WhatsApp
          </a>
        </Button>
      ) : (
        <span className="text-xs text-neutral-600">Cep telefonu yok</span>
      )}
    </li>
  );
}
