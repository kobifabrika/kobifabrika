"use client";

import { useMutation } from "@tanstack/react-query";
import { LogIn, LogOut } from "lucide-react";
import { toast } from "sonner";

import { Notice } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Button } from "@/components/ui/button";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { LEGAL_NOTE } from "@/lib/api/types";
import { istanbulParts } from "@/lib/format";
import { elapsedMinutes, formatDuration, overtimeMinutes } from "@/lib/time-clock";
import { useNow } from "@/lib/use-now";

import { EntryBadge, entryRange, useAttendanceRefresh, useMyClock } from "./attendance-bits";

/**
 * "Mesaim": personelin kendi telefonundan tek düğmeyle mesaiye başlayıp bitirdiği ekran. Düğme telefonda ekranın altında, alt
 * menünün hemen üstünde, tam genişlik (başparmakla tek elle); masaüstünde durum kartının altında.
 */
export function MyClock({ compact = false }: { compact?: boolean }) {
  const clock = useMyClock();
  const refresh = useAttendanceRefresh();
  const now = useNow(30_000);
  const toggle = useMutation({
    mutationFn: (action: "in" | "out") =>
      unwrap(action === "in" ? api.POST("/api/time-clock/clock-in", { body: { staffMemberId: null } }) : api.POST("/api/time-clock/clock-out", { body: { staffMemberId: null } })),
    onSuccess: async (entry, action) => {
      await refresh();
      toast.success(action === "in" ? `Mesai başladı · ${istanbulParts(entry.clockInAt).time}` : `Mesai bitti · ${formatDuration(entry.minutes ?? 0)}`);
    },
    onError: async (e) => {
      toast.error(errorMessage(e));
      await refresh();
    },
  });

  if (clock.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (clock.isError) return <QueryError error={clock.error} onRetry={clock.refetch} />;
  const c = clock.data;
  if (!c.staffMemberId)
    return compact ? null : (
      <Notice>Hesabınız bir personel kaydına bağlı değil. Mesaiye başlamak için işletme sahibi veya yönetici Personel ekranından hesabınızı bağlamalı.</Notice>
    );

  const open = c.open;
  const running = open ? elapsedMinutes(open.clockInAt, now) : 0;
  const week = c.weekMinutes + running;
  const overtime = overtimeMinutes(week, c.overtimeWeeklyHours);
  const button = (
    <Button
      size="lg"
      className="h-14 w-full text-base"
      variant={open ? "outline" : "default"}
      disabled={toggle.isPending}
      onClick={() => toggle.mutate(open ? "out" : "in")}
    >
      {open ? <LogOut aria-hidden className="size-5" /> : <LogIn aria-hidden className="size-5" />}
      {open ? "Mesaiyi bitir" : "Mesaiye başla"}
    </Button>
  );

  return (
    <section aria-label="Mesaim" className={compact ? "flex flex-col gap-3" : "flex flex-col gap-5 pb-24 lg:pb-0"}>
      <div className="flex flex-col gap-1 border border-border p-4" aria-live="polite">
        <span className="text-sm text-neutral-600">{c.staffName}</span>
        {open ? (
          <>
            <span className="text-xl font-bold text-black">Mesaidesiniz</span>
            <span className="text-sm text-black tabular-nums">
              Giriş {istanbulParts(open.clockInAt).time} · {formatDuration(running)}
            </span>
          </>
        ) : (
          <span className="text-xl font-bold text-black">Mesaide değilsiniz</span>
        )}
        <dl className="mt-3 grid grid-cols-2 gap-3 border-t border-border pt-3 text-sm">
          <div>
            <dt className="text-neutral-600">Bugün</dt>
            <dd className="font-semibold text-black tabular-nums">{formatDuration(c.todayMinutes + running)}</dd>
          </div>
          <div>
            <dt className="text-neutral-600">Bu hafta</dt>
            <dd className="font-semibold text-black tabular-nums">
              {formatDuration(week)}
              {overtime > 0 && <span className="block text-xs font-normal text-neutral-700">Fazla mesai {formatDuration(overtime)}</span>}
            </dd>
          </div>
        </dl>
      </div>

      {compact ? (
        button
      ) : (
        <>
          <div className="hidden lg:block lg:max-w-sm">{button}</div>
          <div className="fixed inset-x-0 bottom-[calc(3.5rem+env(safe-area-inset-bottom))] z-20 border-t border-border bg-white px-4 py-3 lg:hidden">
            <div className="mx-auto max-w-xl">{button}</div>
          </div>
        </>
      )}

      {!compact && (
        <>
          <section className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold text-black">Bugünkü kayıtlar</h2>
            {c.today.length === 0 ? (
              <p className="text-sm text-neutral-600">Bugün kayıt yok.</p>
            ) : (
              <ul className="flex flex-col divide-y divide-border border-y border-border">
                {c.today.map((e) => (
                  <li key={e.id} className="flex min-h-12 items-center justify-between gap-3 py-2">
                    <span className="flex items-center gap-2 text-sm text-black tabular-nums">
                      {entryRange(e)} <EntryBadge entry={e} />
                    </span>
                    <span className="text-sm font-semibold text-black tabular-nums">{e.minutes != null ? formatDuration(e.minutes) : ""}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
          <p className="text-xs text-neutral-600">
            Haftalık {c.overtimeWeeklyHours.toLocaleString("tr-TR")} saati aşan süre fazla mesai sayılır. {LEGAL_NOTE}
          </p>
        </>
      )}
    </section>
  );
}
