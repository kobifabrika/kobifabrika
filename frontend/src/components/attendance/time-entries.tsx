"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Pencil, Plus, Trash2 } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { ResponsivePanel } from "@/components/appointments/appointment-bits";
import { selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { useStaffList } from "@/components/staff/staff-bits";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { LEGAL_NOTE, type TimeEntry } from "@/lib/api/types";
import { longDayLabel } from "@/lib/calendar";
import { addDays } from "@/lib/date-ranges";
import { istanbulParts, istanbulToUtcIso, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { formatDuration } from "@/lib/time-clock";

import { EntryBadge, entryRange, timeEntriesKey, useAttendanceRefresh } from "./attendance-bits";

const isDay = (v: string | null): v is string => !!v && /^\d{4}-\d{2}-\d{2}$/.test(v);

/** Yönetici/Mali Müşavir: seçilen günün mesai kayıtları personel personel; Sahip/Yönetici ekler, düzeltir, siler. */
export function TimeEntriesDay() {
  const { role } = useSession();
  const manage = can(role, "manageStaffTime");
  const router = useRouter();
  const params = useSearchParams();
  const today = todayIstanbul();
  const day = isDay(params.get("tarih")) ? params.get("tarih")! : today;
  const [editing, setEditing] = useState<{ entry?: TimeEntry; staffId?: string } | null>(null);
  const refresh = useAttendanceRefresh();
  const staff = useStaffList();
  const entries = useQuery({
    queryKey: [...timeEntriesKey, day],
    queryFn: () => unwrap(api.GET("/api/time-entries", { params: { query: { from: day, to: day } } })),
  });
  const clock = useMutation({
    mutationFn: ({ staffId, action }: { staffId: string; action: "in" | "out" }) =>
      unwrap(
        action === "in"
          ? api.POST("/api/time-clock/clock-in", { body: { staffMemberId: staffId } })
          : api.POST("/api/time-clock/clock-out", { body: { staffMemberId: staffId } }),
      ),
    onSuccess: async (_, v) => {
      await refresh();
      toast.success(v.action === "in" ? "Mesai başlatıldı." : "Mesai bitirildi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const go = (d: string) => {
    const next = new URLSearchParams(params.toString());
    next.set("sekme", "mesai");
    if (d === today) next.delete("tarih");
    else next.set("tarih", d);
    router.replace(`?${next.toString()}`, { scroll: false });
  };

  const rows = (staff.data ?? []).map((s) => ({ staff: s, items: (entries.data ?? []).filter((e) => e.staffMemberId === s.id) }));
  // Pasif/silinmiş personelin kaydı da görünsün.
  const extra = (entries.data ?? []).filter((e) => !staff.data?.some((s) => s.id === e.staffMemberId));

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <Button variant="outline" size="icon" aria-label="Önceki gün" onClick={() => go(addDays(day, -1))}>
          <ChevronLeft aria-hidden />
        </Button>
        <Input type="date" aria-label="Gün" value={day} max={today} onChange={(e) => isDay(e.target.value) && go(e.target.value)} className="w-44" />
        <Button variant="outline" size="icon" aria-label="Sonraki gün" disabled={day >= today} onClick={() => go(addDays(day, 1))}>
          <ChevronRight aria-hidden />
        </Button>
        <span className="text-sm font-semibold text-black">{longDayLabel(day)}</span>
        {manage && (
          <Button className="ml-auto max-sm:w-full" onClick={() => setEditing({})}>
            <Plus aria-hidden />
            Mesai ekle
          </Button>
        )}
      </div>

      {staff.isPending || entries.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : staff.isError || entries.isError ? (
        <QueryError error={staff.error ?? entries.error} onRetry={() => (staff.refetch(), entries.refetch())} />
      ) : rows.length === 0 ? (
        <p className="text-sm text-neutral-600">Henüz personel yok.</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {rows.map(({ staff: s, items }) => {
            const open = items.find((e) => !e.clockOutAt && !e.forgotten);
            const total = items.reduce((sum, e) => sum + (e.minutes ?? 0), 0);
            return (
              <li key={s.id} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="flex min-w-0 flex-col gap-1">
                  <span className="text-sm font-semibold text-black">{s.fullName}</span>
                  {items.length === 0 ? (
                    <span className="text-sm text-neutral-600">Kayıt yok</span>
                  ) : (
                    items.map((e) => <EntryLine key={e.id} entry={e} manage={manage} onEdit={() => setEditing({ entry: e })} />)
                  )}
                </div>
                <div className="flex shrink-0 items-center gap-2 sm:flex-col sm:items-end">
                  {total > 0 && <span className="text-sm font-semibold text-black tabular-nums">{formatDuration(total)}</span>}
                  {manage && day === today && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="min-h-11"
                      disabled={clock.isPending}
                      onClick={() => clock.mutate({ staffId: s.id, action: open ? "out" : "in" })}
                    >
                      {open ? "Çıkış yap" : "Giriş yap"}
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
          {extra.map((e) => (
            <li key={e.id} className="flex flex-col gap-1 py-3">
              <span className="text-sm font-semibold text-black">{e.staffName}</span>
              <EntryLine entry={e} manage={manage} onEdit={() => setEditing({ entry: e })} />
            </li>
          ))}
        </ul>
      )}
      <p className="text-xs text-neutral-600">{LEGAL_NOTE}</p>
      {editing && <EntryDialog entry={editing.entry} defaultDay={day} onClose={() => setEditing(null)} />}
    </div>
  );
}

function EntryLine({ entry, manage, onEdit }: { entry: TimeEntry; manage: boolean; onEdit: () => void }) {
  return (
    <span className="flex flex-wrap items-center gap-2 text-sm text-black tabular-nums">
      {entryRange(entry)}
      {entry.minutes != null && <span className="text-neutral-600">({formatDuration(entry.minutes)})</span>}
      <EntryBadge entry={entry} />
      {entry.inSource === "Manager" && <span className="text-xs text-neutral-600">yönetici girdi</span>}
      {entry.note && <span className="text-neutral-600">· {entry.note}</span>}
      {manage && (
        <Button variant="ghost" size="sm" className="min-h-11" onClick={onEdit} aria-label={`${entryRange(entry)} kaydını düzelt`}>
          <Pencil aria-hidden />
          {entry.forgotten && !entry.clockOutAt ? "Çıkışı gir" : "Düzelt"}
        </Button>
      )}
    </span>
  );
}

/** Ekleme (personel seçilir) veya düzeltme. Çıkış saati girişten küçükse ertesi gün sayılır (gece mesaisi). */
function EntryDialog({ entry, defaultDay, onClose }: { entry?: TimeEntry; defaultDay: string; onClose: () => void }) {
  const refresh = useAttendanceRefresh();
  const staff = useStaffList();
  const start = entry ? istanbulParts(entry.clockInAt) : null;
  const [staffId, setStaffId] = useState(entry?.staffMemberId ?? "");
  const [day, setDay] = useState(start?.date ?? defaultDay);
  const [inTime, setInTime] = useState(start?.time ?? "09:00");
  const [outTime, setOutTime] = useState(entry?.clockOutAt ? istanbulParts(entry.clockOutAt).time : entry ? "" : "18:00");
  const [note, setNote] = useState(entry?.note ?? "");
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: (body: { clockInAt: string; clockOutAt: string | null; note: string | null }) =>
      unwrap(
        entry
          ? api.PUT("/api/time-entries/{id}", { params: { path: { id: entry.id } }, body })
          : api.POST("/api/time-entries", { body: { ...body, staffMemberId: staffId } }),
      ),
    onSuccess: async () => {
      await refresh();
      toast.success(entry ? "Mesai düzeltildi." : "Mesai eklendi.");
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/time-entries/{id}", { params: { path: { id: entry!.id } } })),
    onSuccess: async () => {
      await refresh();
      toast.success("Mesai kaydı silindi.");
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!entry && !staffId) return setError("Personel seçin.");
    if (entry?.clockOutAt && !outTime) return setError("Kapanmış mesainin çıkış saati silinemez.");
    const clockInAt = istanbulToUtcIso(day, inTime);
    const clockOutAt = outTime ? istanbulToUtcIso(outTime <= inTime ? addDays(day, 1) : day, outTime) : null;
    save.mutate({ clockInAt, clockOutAt, note: note.trim() || null });
  };

  return (
    <ResponsivePanel
      open
      onOpenChange={(o) => !o && onClose()}
      title={entry ? `${entry.staffName}: mesaiyi düzelt` : "Mesai ekle"}
      description={entry ? "Eski ve yeni saat aktivite günlüğüne yazılır." : "Unutulan veya kart dışı mesai için."}
    >
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        {!entry && (
          <Field label="Personel">
            <select className={selectClass} value={staffId} onChange={(e) => setStaffId(e.target.value)}>
              <option value="">Seçin</option>
              {staff.data?.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName}
                </option>
              ))}
            </select>
          </Field>
        )}
        <Field label="Gün">
          <Input type="date" value={day} max={todayIstanbul()} onChange={(e) => setDay(e.target.value)} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Giriş">
            <Input type="time" value={inTime} onChange={(e) => setInTime(e.target.value)} />
          </Field>
          <Field label="Çıkış" hint={entry?.clockOutAt ? undefined : "Boş: açık mesai"}>
            <Input type="time" value={outTime} onChange={(e) => setOutTime(e.target.value)} />
          </Field>
        </div>
        {outTime && outTime <= inTime && <p className="text-sm text-neutral-600">Çıkış ertesi gün sayılır (gece mesaisi).</p>}
        <Field label="Not (isteğe bağlı)">
          <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} />
        </Field>
        {error && <FormMessage>{error}</FormMessage>}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={save.isPending}>
            Kaydet
          </Button>
          {entry && (
            <Button
              type="button"
              variant="ghost"
              className="text-danger-600"
              disabled={remove.isPending}
              onClick={() => window.confirm("Bu mesai kaydı silinsin mi?") && remove.mutate()}
            >
              <Trash2 aria-hidden />
              Sil
            </Button>
          )}
        </div>
      </form>
    </ResponsivePanel>
  );
}
