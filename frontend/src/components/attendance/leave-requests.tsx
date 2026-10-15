"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { CalendarPlus } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import { ResponsivePanel } from "@/components/appointments/appointment-bits";
import { Notice } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { halfDayNames, type LeavePreview, type LeaveRequest } from "@/lib/api/types";
import { formatDateTime, istanbulParts } from "@/lib/format";
import { can } from "@/lib/permissions";
import { dateRangeText, daysText } from "@/lib/time-clock";

import { LeaveStatusBadge, leaveKey, useAttendanceRefresh } from "./attendance-bits";

export function describeLeave(r: Pick<LeaveRequest, "startDate" | "endDate" | "halfDay" | "days">): string {
  const range = dateRangeText(r.startDate, r.endDate);
  return r.halfDay === "None" ? `${range} · ${daysText(r.days)}` : `${range} · ${halfDayNames[r.halfDay].toLocaleLowerCase("tr-TR")} (yarım gün)`;
}

/**
 * İzin talepleri. Personel kendi taleplerini görür ve bekleyenini geri çeker; Sahip/Yönetici "Bekleyen" süzgeciyle karar verir
 * (onay penceresinde gün, bakiye ve çakışan randevular). ?talep={id} bildirimden gelince pencereyi açar.
 */
export function LeaveRequests({ mine = false }: { mine?: boolean }) {
  const { role } = useSession();
  const router = useRouter();
  const params = useSearchParams();
  const [filter, setFilter] = useState<"pending" | "all">(mine ? "all" : "pending");
  const deciding = params.get("talep");
  const list = useQuery({
    queryKey: [...leaveKey, "requests"],
    queryFn: () => unwrap(api.GET("/api/leave-requests")),
  });

  const setDeciding = (id: string | null) => {
    const next = new URLSearchParams(params.toString());
    if (id) next.set("talep", id);
    else next.delete("talep");
    router.replace(`?${next.toString()}`, { scroll: false });
  };

  const items = (list.data?.items ?? []).filter((r) => filter === "all" || r.status === "Pending");
  const decidingItem = list.data?.items.find((r) => r.id === deciding && r.canDecide);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        {!mine && list.data ? (
          <ScrollRow aria-label="İzin talebi süzgeci" activeKey={filter}>
            <Chip active={filter === "pending"} onClick={() => setFilter("pending")}>
              Bekleyen {list.data.pendingCount}
            </Chip>
            <Chip active={filter === "all"} onClick={() => setFilter("all")}>
              Tümü
            </Chip>
          </ScrollRow>
        ) : (
          <span />
        )}
        {can(role, "clockInOut") && (
          <Button asChild className="max-sm:w-full">
            <Link href="/panel/mesai-izin/izin-talebi">
              <CalendarPlus aria-hidden />
              {mine ? "İzin talep et" : "İzin gir"}
            </Link>
          </Button>
        )}
      </div>

      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        <p className="text-sm text-neutral-600">{filter === "pending" ? "Bekleyen izin talebi yok." : "Henüz izin talebi yok."}</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {items.map((r) => (
            <RequestRow key={r.id} request={r} showStaff={!mine} onDecide={() => setDeciding(r.id)} />
          ))}
        </ul>
      )}
      {decidingItem && <DecisionDialog request={decidingItem} onClose={() => setDeciding(null)} />}
    </div>
  );
}

function RequestRow({ request: r, showStaff, onDecide }: { request: LeaveRequest; showStaff: boolean; onDecide: () => void }) {
  const refresh = useAttendanceRefresh();
  const cancel = useMutation({
    mutationFn: () => unwrap(api.POST("/api/leave-requests/{id}/cancel", { params: { path: { id: r.id } } })),
    onSuccess: async () => {
      await refresh();
      toast.success(r.status === "Approved" ? "İzin iptal edildi; takvimde yeniden açık." : "Talep geri çekildi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <li className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex min-w-0 flex-col gap-1">
        <span className="flex flex-wrap items-center gap-2 text-sm font-semibold text-black">
          {showStaff && <span>{r.staffName}</span>}
          <span>{r.typeName}</span>
          <LeaveStatusBadge status={r.status} />
        </span>
        <span className="text-sm text-black tabular-nums">{describeLeave(r)}</span>
        {r.note && <span className="text-sm text-neutral-600">Not: {r.note}</span>}
        {r.decisionNote && <span className="text-sm text-neutral-600">Yanıt: {r.decisionNote}</span>}
        <span className="text-xs text-neutral-600">
          {r.requestedByName ? `${r.requestedByName} · ` : ""}
          {formatDateTime(r.createdAt)}
          {r.decidedByName ? ` · Karar: ${r.decidedByName}` : ""}
        </span>
      </div>
      <div className="flex shrink-0 flex-wrap gap-2">
        {r.canDecide && <Button onClick={onDecide}>Karar ver</Button>}
        {r.canCancel && (
          <Button
            variant="outline"
            disabled={cancel.isPending}
            onClick={() =>
              window.confirm(r.status === "Approved" ? "Onaylı izin iptal edilsin mi? Takvimdeki kapalı zaman kalkar." : "Talep geri çekilsin mi?") &&
              cancel.mutate()
            }
          >
            {r.status === "Approved" ? "İzni iptal et" : "Geri çek"}
          </Button>
        )}
      </div>
    </li>
  );
}

/** Çakışan randevular ve bakiye özeti (talep formu ve onay penceresi ortak). */
export function PreviewSummary({ preview, staffName }: { preview: LeavePreview; staffName?: string }) {
  return (
    <div className="flex flex-col gap-3">
      <dl className="grid grid-cols-2 gap-3 border border-border p-3 text-sm">
        <div>
          <dt className="text-neutral-600">İzin günü</dt>
          <dd className="font-semibold text-black tabular-nums">{preview.days > 0 ? daysText(preview.days) : "Çalışma günü yok"}</dd>
        </div>
        {preview.isAnnual && preview.balanceAfter != null && (
          <div>
            <dt className="text-neutral-600">Yıllık izin bakiyesi</dt>
            <dd className="font-semibold text-black tabular-nums">
              {daysText(preview.balanceBefore ?? 0)} → {daysText(preview.balanceAfter)}
            </dd>
          </div>
        )}
      </dl>
      {preview.isAnnual && preview.balanceAfter != null && preview.balanceAfter < 0 && (
        <Notice>Bakiye {daysText(preview.balanceAfter)} olacak (avans izin). Onay engellenmez.</Notice>
      )}
      {preview.overlap && <FormMessage>{preview.overlap}</FormMessage>}
      {preview.appointments.length > 0 && (
        <section className="flex flex-col gap-2" aria-label="Çakışan randevular">
          <Notice>
            Bu günlerde {preview.appointments.length} randevu var{staffName ? ` (${staffName})` : ""}. Randevular otomatik iptal edilmez; takvimden
            başka personele veya güne taşıyın.
          </Notice>
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {preview.appointments.map((a) => (
              <li key={a.appointmentId} className="flex min-h-11 items-center justify-between gap-3 py-1 text-sm">
                <span className="text-black">
                  {a.contactName}
                  {a.serviceName ? ` · ${a.serviceName}` : ""}
                </span>
                <Link
                  href={`/panel/randevular?gorunum=gun&tarih=${istanbulParts(a.startAt).date}&randevu=${a.appointmentId}`}
                  className="shrink-0 text-black underline decoration-brand underline-offset-4 tabular-nums"
                >
                  {formatDateTime(a.startAt)}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function DecisionDialog({ request, onClose }: { request: LeaveRequest; onClose: () => void }) {
  const refresh = useAttendanceRefresh();
  const [note, setNote] = useState("");
  const preview = useQuery({
    queryKey: [...leaveKey, "preview", request.id],
    queryFn: () => unwrap(api.GET("/api/leave-requests/{id}/preview", { params: { path: { id: request.id } } })),
  });
  const decide = useMutation({
    mutationFn: (action: "approve" | "reject") =>
      unwrap(
        action === "approve"
          ? api.POST("/api/leave-requests/{id}/approve", { params: { path: { id: request.id } }, body: { note: note.trim() || null } })
          : api.POST("/api/leave-requests/{id}/reject", { params: { path: { id: request.id } }, body: { note: note.trim() || null } }),
      ),
    onSuccess: async (_, action) => {
      await refresh();
      toast.success(action === "approve" ? "İzin onaylandı; takvimde kapalı görünür." : "Talep reddedildi.");
      onClose();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <ResponsivePanel
      open
      onOpenChange={(o) => !o && onClose()}
      title={`${request.staffName} · ${request.typeName}`}
      description={describeLeave(request)}
      wide
    >
      <div className="flex flex-col gap-4 pb-2">
        {request.note && <p className="text-sm text-black">Not: {request.note}</p>}
        {preview.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : preview.isError ? (
          <QueryError error={preview.error} onRetry={preview.refetch} />
        ) : (
          <PreviewSummary preview={preview.data} staffName={request.staffName} />
        )}
        <Field label="Yanıt (isteğe bağlı)">
          <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} placeholder="Ör. O hafta yoğunuz" />
        </Field>
        <div className="flex flex-wrap gap-2">
          <Button disabled={decide.isPending} onClick={() => decide.mutate("approve")} className="max-sm:flex-1">
            Onayla
          </Button>
          <Button variant="outline" disabled={decide.isPending} onClick={() => decide.mutate("reject")} className="max-sm:flex-1">
            Reddet
          </Button>
        </div>
      </div>
    </ResponsivePanel>
  );
}
