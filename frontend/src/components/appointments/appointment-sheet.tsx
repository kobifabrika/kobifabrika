"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { CalendarClock, Phone, Receipt, UserRound } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import type { Appointment } from "@/lib/api/types";
import { longDayLabel } from "@/lib/calendar";
import { displayPhone, telLink } from "@/lib/contacts";
import { istanbulParts } from "@/lib/format";
import { can } from "@/lib/permissions";
import { useIsDesktop } from "@/lib/use-media";
import { useNow } from "@/lib/use-now";

import { StatusBadge, appointmentsKey, useAppointmentRefresh } from "./appointment-bits";

type Action = "confirm" | "complete" | "no-show" | "reopen" | "cancel";

/** "14 Ekim Çarşamba · 14:00-14:45" */
export function appointmentWhen(a: Pick<Appointment, "startAt" | "endAt">): string {
  const s = istanbulParts(a.startAt);
  return `${longDayLabel(s.date)} · ${s.time}-${istanbulParts(a.endAt).time}`;
}

/**
 * Randevu ayrıntısı (masaüstünde sağdan, telefonda alttan): durum düğmeleri, "Taşı", "Satışa çevir", müşteri kartı.
 * Tamamlandı/Gelmedi başlangıç saatinden sonra işaretlenir; satışa çevrilen randevu geri alınamaz.
 */
export function AppointmentSheet({
  appointmentId,
  initial,
  onClose,
  onMove,
}: {
  appointmentId: string | null;
  initial?: Appointment;
  onClose: () => void;
  onMove: (appointment: Appointment) => void;
}) {
  const desktop = useIsDesktop();
  const { role } = useSession();
  const refresh = useAppointmentRefresh();
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState("");
  const query = useQuery({
    queryKey: [...appointmentsKey, appointmentId],
    queryFn: () => unwrap(api.GET("/api/appointments/{id}", { params: { path: { id: appointmentId! } } })),
    enabled: !!appointmentId,
    initialData: initial,
  });
  const act = useMutation({
    mutationFn: async (action: Action) => {
      const path = { params: { path: { id: appointmentId! } } };
      switch (action) {
        case "confirm":
          return unwrap(api.POST("/api/appointments/{id}/confirm", path));
        case "complete":
          return unwrap(api.POST("/api/appointments/{id}/complete", path));
        case "no-show":
          return unwrap(api.POST("/api/appointments/{id}/no-show", path));
        case "reopen":
          return unwrap(api.POST("/api/appointments/{id}/reopen", path));
        case "cancel":
          return unwrap(api.POST("/api/appointments/{id}/cancel", { ...path, body: { reason: reason.trim() || null } }));
      }
    },
    onSuccess: async (_, action) => {
      await refresh();
      setCancelling(false);
      toast.success(
        { confirm: "Randevu onaylandı.", complete: "Randevu tamamlandı.", "no-show": "Gelmedi olarak işaretlendi.", reopen: "Geri alındı.", cancel: "Randevu iptal edildi." }[
          action
        ],
      );
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const a = query.data;
  const write = can(role, "writeAppointments");
  const now = useNow(30_000);
  const started = a ? Date.parse(a.startAt) <= now : false;

  return (
    <Sheet open={!!appointmentId} onOpenChange={(o) => !o && onClose()}>
      <SheetContent
        side={desktop ? "right" : "bottom"}
        className={desktop ? "w-full overflow-y-auto sm:max-w-md" : "max-h-[92vh] gap-0 overflow-y-auto rounded-t-xl pb-[calc(1rem+env(safe-area-inset-bottom))]"}
      >
        <SheetHeader>
          <SheetTitle>{a ? a.contactName : "Randevu"}</SheetTitle>
          <SheetDescription>{a ? `${a.serviceName} · ${a.staffName}` : query.isError ? "Açılamadı" : "Yükleniyor…"}</SheetDescription>
        </SheetHeader>
        {query.isError && (
          <div className="px-4 pb-4">
            <FormMessage>{errorMessage(query.error)}</FormMessage>
          </div>
        )}
        {a && (
          <div className="flex flex-col gap-4 px-4 pb-4">
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={a.status} />
              {a.source === "Online" && <span className="text-xs text-neutral-600">Online randevu</span>}
            </div>
            <p className="flex items-center gap-2 text-sm font-semibold text-black tabular-nums">
              <CalendarClock aria-hidden className="size-4" />
              {appointmentWhen(a)}
            </p>
            {a.note && <p className="text-sm whitespace-pre-line text-neutral-700">{a.note}</p>}
            {a.cancelReason && <p className="text-sm text-neutral-700">İptal nedeni: {a.cancelReason}</p>}
            <div className="flex flex-wrap gap-2">
              <Button asChild variant="outline">
                <Link href={`/panel/musteriler/${a.contactId}`}>
                  <UserRound aria-hidden />
                  Müşteri kartı
                </Link>
              </Button>
              {a.contactPhone && (
                <Button asChild variant="outline">
                  <a href={telLink(a.contactPhone)}>
                    <Phone aria-hidden />
                    {displayPhone(a.contactPhone)}
                  </a>
                </Button>
              )}
              {a.saleId && (
                <Button asChild variant="outline">
                  <Link href={`/panel/satislar/${a.saleId}`}>
                    <Receipt aria-hidden />
                    {a.saleNo}
                  </Link>
                </Button>
              )}
            </div>

            {write && (
              <div className="flex flex-col gap-2 border-t border-border pt-4">
                {a.status === "Pending" && <Button onClick={() => act.mutate("confirm")} disabled={act.isPending}>Onayla</Button>}
                {a.status === "Confirmed" && (
                  <>
                    <Button onClick={() => act.mutate("complete")} disabled={act.isPending || !started}>
                      Tamamlandı
                    </Button>
                    <Button variant="outline" onClick={() => act.mutate("no-show")} disabled={act.isPending || !started}>
                      Gelmedi
                    </Button>
                    {!started && <p className="text-xs text-neutral-600">Tamamlandı ve Gelmedi randevu saati gelince işaretlenir.</p>}
                  </>
                )}
                {a.status === "Completed" && !a.saleId && can(role, "createSales") && (
                  <Button asChild>
                    <Link href={`/panel/satislar/yeni?randevu=${a.id}`}>Satışa çevir</Link>
                  </Button>
                )}
                {(a.status === "Pending" || a.status === "Confirmed") && (
                  <Button variant="outline" onClick={() => onMove(a)}>
                    Taşı
                  </Button>
                )}
                {((a.status === "Completed" && !a.saleId) || a.status === "NoShow") && (
                  <Button variant="ghost" onClick={() => act.mutate("reopen")} disabled={act.isPending}>
                    Geri al
                  </Button>
                )}
                {(a.status === "Pending" || a.status === "Confirmed") &&
                  (cancelling ? (
                    <div className="flex flex-col gap-2 border border-border p-3">
                      <Field label="İptal nedeni (isteğe bağlı)">
                        <Textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
                      </Field>
                      <div className="flex gap-2">
                        <Button variant="destructive" onClick={() => act.mutate("cancel")} disabled={act.isPending}>
                          Randevuyu iptal et
                        </Button>
                        <Button variant="ghost" onClick={() => setCancelling(false)}>
                          Vazgeç
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <Button variant="ghost" className="text-danger-600" onClick={() => setCancelling(true)}>
                      İptal et
                    </Button>
                  ))}
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
