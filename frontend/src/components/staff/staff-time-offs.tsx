"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { ChoiceGroup } from "@/components/catalog/catalog-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { timeOffKindNames, type TimeOff, type TimeOffKind } from "@/lib/api/types";
import { addDays } from "@/lib/date-ranges";
import { formatDayShort, istanbulParts, istanbulToUtcIso, todayIstanbul } from "@/lib/format";

import { staffKey, useStaffRefresh } from "./staff-bits";

/** "14.10.2026 12:00 – 13:00" veya tam günlerde "14.10.2026 – 16.10.2026 (tüm gün)". */
export function timeOffLabel(t: Pick<TimeOff, "startAt" | "endAt">): string {
  const s = istanbulParts(t.startAt);
  const e = istanbulParts(t.endAt);
  if (s.time === "00:00" && e.time === "00:00") {
    const last = addDays(e.date, -1);
    return last === s.date ? `${formatDayShort(s.date)} (tüm gün)` : `${formatDayShort(s.date)} – ${formatDayShort(last)} (tüm gün)`;
  }
  return s.date === e.date
    ? `${formatDayShort(s.date)} ${s.time} – ${e.time}`
    : `${formatDayShort(s.date)} ${s.time} – ${formatDayShort(e.date)} ${e.time}`;
}

/**
 * Personelin kapalı zamanları (mola, izin). Takvimde gri görünür, o saatlere online randevu verilmez.
 * Sahip/Yönetici her türü ekler; Personel sadece kendine mola ekler (ownBreakOnly).
 */
export function StaffTimeOffs({ staffId, readOnly, ownBreakOnly = false }: { staffId: string; readOnly: boolean; ownBreakOnly?: boolean }) {
  const refresh = useStaffRefresh();
  const list = useQuery({
    queryKey: [...staffKey, staffId, "time-offs"],
    queryFn: () => unwrap(api.GET("/api/staff/{id}/time-offs", { params: { path: { id: staffId } } })),
  });
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/time-offs/{id}", { params: { path: { id } } })),
    onSuccess: async () => {
      await refresh();
      toast.success("Kapalı zaman silindi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <div className="flex flex-col gap-5">
      {!readOnly && <TimeOffForm staffId={staffId} ownBreakOnly={ownBreakOnly} />}
      <section className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-black">Yaklaşan kapalı zamanlar</h3>
        {list.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : list.isError ? (
          <QueryError error={list.error} onRetry={list.refetch} />
        ) : list.data.length === 0 ? (
          <p className="text-sm text-neutral-600">Önümüzdeki 90 günde kapalı zaman yok.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {list.data.map((t) => (
              <li key={t.id} className="flex min-h-12 items-center gap-2 py-1">
                <span className="flex flex-1 flex-col">
                  <span className="text-sm font-semibold text-black">
                    {timeOffKindNames[t.kind]}
                    {t.note ? ` · ${t.note}` : ""}
                  </span>
                  <span className="text-sm text-neutral-600 tabular-nums">{timeOffLabel(t)}</span>
                </span>
                {!readOnly && (!ownBreakOnly || t.kind === "Break") && (
                  <Button variant="ghost" size="icon" aria-label={`${timeOffLabel(t)} kapalı zamanını sil`} onClick={() => remove.mutate(t.id)}>
                    <Trash2 aria-hidden />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function TimeOffForm({ staffId, ownBreakOnly }: { staffId: string; ownBreakOnly: boolean }) {
  const refresh = useStaffRefresh();
  const today = todayIstanbul();
  const [kind, setKind] = useState<TimeOffKind>("Break");
  const [allDay, setAllDay] = useState(false);
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [start, setStart] = useState("12:00");
  const [end, setEnd] = useState("13:00");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: (body: { startAt: string; endAt: string; kind: TimeOffKind; note: string | null }) =>
      unwrap(api.POST("/api/staff/{id}/time-offs", { params: { path: { id: staffId } }, body })),
    onSuccess: async () => {
      await refresh();
      setNote("");
      setError(null);
      toast.success("Kapalı zaman eklendi.");
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const startAt = allDay ? istanbulToUtcIso(from, "00:00") : istanbulToUtcIso(from, start);
    const endAt = allDay ? istanbulToUtcIso(addDays(to, 1), "00:00") : istanbulToUtcIso(from, end);
    if (endAt <= startAt) return setError("Bitiş başlangıçtan sonra olmalı.");
    save.mutate({ startAt, endAt, kind: ownBreakOnly ? "Break" : kind, note: note.trim() || null });
  };

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-4 border border-border p-4">
      <h3 className="text-sm font-semibold text-black">Kapalı zaman ekle</h3>
      {!ownBreakOnly && (
        <ChoiceGroup
          label="Tür"
          options={(Object.keys(timeOffKindNames) as TimeOffKind[]).map((k) => ({ value: k, label: timeOffKindNames[k] }))}
          value={kind}
          onChange={(k) => {
            setKind(k);
            setAllDay(k === "Leave");
          }}
        />
      )}
      <label className="flex min-h-11 items-center gap-3 text-sm text-black">
        <Switch checked={allDay} onCheckedChange={setAllDay} />
        Tüm gün
      </label>
      <div className="flex flex-wrap gap-3">
        <Field label={allDay ? "İlk gün" : "Gün"} className="w-44">
          <Input type="date" value={from} min={today} onChange={(e) => (setFrom(e.target.value), to < e.target.value && setTo(e.target.value))} />
        </Field>
        {allDay ? (
          <Field label="Son gün" className="w-44">
            <Input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
          </Field>
        ) : (
          <>
            <Field label="Başlangıç" className="w-32">
              <Input type="time" value={start} onChange={(e) => setStart(e.target.value)} />
            </Field>
            <Field label="Bitiş" className="w-32">
              <Input type="time" value={end} onChange={(e) => setEnd(e.target.value)} />
            </Field>
          </>
        )}
      </div>
      <Field label="Not (isteğe bağlı)">
        <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} placeholder="Ör. Öğle arası, yıllık izin" />
      </Field>
      {error && <FormMessage>{error}</FormMessage>}
      <Button type="submit" variant="outline" className="self-start" disabled={save.isPending}>
        Ekle
      </Button>
    </form>
  );
}
