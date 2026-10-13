"use client";

import { useQuery } from "@tanstack/react-query";
import { CalendarDays } from "lucide-react";
import Link from "next/link";

import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { api, unwrap } from "@/lib/api/client";
import { istanbulParts } from "@/lib/format";
import { formatDayShort } from "@/lib/format";

import { StatusBadge, appointmentsKey } from "./appointment-bits";

/** Müşterinin randevuları ve gelmeyen müşteri uyarısı (Randevular modülü açıksa). */
export function useContactAppointments(contactId: string, enabled: boolean) {
  return useQuery({
    queryKey: [...appointmentsKey, "contact", contactId],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}/appointments", { params: { path: { id: contactId } } })),
    enabled,
  });
}

/** Müşteri kartı → Randevular sekmesi: yaklaşan ve geçmiş randevular (yeniden eskiye). */
export function ContactAppointmentList({ contactId }: { contactId: string }) {
  const list = useContactAppointments(contactId, true);
  if (list.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (list.isError) return <QueryError error={list.error} onRetry={list.refetch} />;
  if (list.data.items.length === 0)
    return (
      <EmptyState icon={<CalendarDays aria-hidden className="size-8" />} title="Randevu yok">
        <p className="text-sm text-neutral-600">Bu müşterinin henüz randevusu yok.</p>
      </EmptyState>
    );

  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {list.data.items.map((a) => {
        const s = istanbulParts(a.startAt);
        return (
          <li key={a.id}>
            <Link
              href={`/panel/randevular?tarih=${s.date}&randevu=${a.id}`}
              className="flex min-h-14 flex-wrap items-center gap-x-3 gap-y-1 py-2 hover:bg-neutral-100"
            >
              <span className="w-36 text-sm font-semibold text-black tabular-nums">
                {formatDayShort(s.date)} {s.time}
              </span>
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate text-sm text-black">{a.serviceName}</span>
                <span className="truncate text-xs text-neutral-600">
                  {a.staffName}
                  {a.saleNo ? ` · ${a.saleNo}` : ""}
                </span>
              </span>
              <StatusBadge status={a.status} />
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
