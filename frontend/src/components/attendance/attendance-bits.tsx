"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";

import { api, unwrap } from "@/lib/api/client";
import { leaveStatusNames, type LeaveStatus, type TimeEntry } from "@/lib/api/types";
import { istanbulParts } from "@/lib/format";

export const clockKey = ["time-clock"] as const;
export const timeEntriesKey = ["time-entries"] as const;
export const leaveKey = ["leave"] as const;

/** Mesaim: bağlı personel kaydı, açık mesai, bugün ve bu hafta. */
export function useMyClock(enabled = true) {
  return useQuery({ queryKey: [...clockKey, "me"], queryFn: () => unwrap(api.GET("/api/time-clock/me")), enabled });
}

export function useLeaveTypes(includeInactive = false) {
  return useQuery({
    queryKey: [...leaveKey, "types", includeInactive],
    queryFn: () => unwrap(api.GET("/api/leave-types", { params: { query: { includeInactive } } })),
    staleTime: 60_000,
  });
}

/** Mesai, izin, takvim ve Bugün ekranlarını birlikte yeniler. */
export function useAttendanceRefresh() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: clockKey }),
      queryClient.invalidateQueries({ queryKey: timeEntriesKey }),
      queryClient.invalidateQueries({ queryKey: leaveKey }),
      queryClient.invalidateQueries({ queryKey: ["calendar"] }),
      queryClient.invalidateQueries({ queryKey: ["staff"] }),
      queryClient.invalidateQueries({ queryKey: ["dashboard"] }),
      queryClient.invalidateQueries({ queryKey: ["notifications"] }),
    ]);
}

/** "09:02 – 18:10" veya açıkken "09:02 – …"; başka güne taşan çıkışta tarih de yazılır. */
export function entryRange(e: Pick<TimeEntry, "clockInAt" | "clockOutAt">): string {
  const start = istanbulParts(e.clockInAt);
  if (!e.clockOutAt) return `${start.time} – …`;
  const end = istanbulParts(e.clockOutAt);
  return end.date === start.date ? `${start.time} – ${end.time}` : `${start.time} – ${end.time} (ertesi gün)`;
}

/** Durum etiketi: bekleyen gri kutu + mavi çizgi; onaylı siyah; ret/iptal çerçeveli. Renk kuralı: docs/04-tasarim.md. */
export function LeaveStatusBadge({ status, className }: { status: LeaveStatus; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold",
        status === "Pending" && "border-l-2 border-brand bg-neutral-100 text-black",
        status === "Approved" && "bg-black text-white",
        (status === "Rejected" || status === "Cancelled") && "border border-neutral-400 text-neutral-700",
        className,
      )}
    >
      {leaveStatusNames[status]}
    </span>
  );
}

/** Mesai kaydı etiketleri: açık → "Mesaide", unutulmuş → "Çıkış unutuldu". */
export function EntryBadge({ entry }: { entry: Pick<TimeEntry, "clockOutAt" | "forgotten"> }) {
  if (entry.forgotten && !entry.clockOutAt)
    return <span className="inline-flex border-l-2 border-brand bg-neutral-100 px-2 py-0.5 text-xs font-semibold text-black">Çıkış unutuldu</span>;
  if (entry.forgotten) return <span className="inline-flex border border-neutral-400 px-2 py-0.5 text-xs text-neutral-700">Çıkış sonradan girildi</span>;
  if (!entry.clockOutAt) return <span className="inline-flex bg-black px-2 py-0.5 text-xs font-semibold text-white">Mesaide</span>;
  return null;
}
