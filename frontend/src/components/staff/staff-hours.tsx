"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { Copy, Plus, X } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";

import { QueryError } from "@/components/feedback/query-error";
import { FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import type { StaffDay } from "@/lib/api/types";
import { parseHhmm } from "@/lib/calendar";

import { staffKey, useStaffRefresh } from "./staff-bits";

const dayNames: Record<string, string> = {
  Monday: "Pazartesi",
  Tuesday: "Salı",
  Wednesday: "Çarşamba",
  Thursday: "Perşembe",
  Friday: "Cuma",
  Saturday: "Cumartesi",
  Sunday: "Pazar",
};

export const MAX_RANGES = 4;

/** Günün aralıklarında hata varsa Türkçe açıklama (sunucuyla aynı kurallar). */
export function dayError(day: StaffDay): string | null {
  const ranges = day.ranges.filter((r) => r.start && r.end);
  if (ranges.length !== day.ranges.length) return "Saatleri girin.";
  if (ranges.some((r) => parseHhmm(r.start) >= parseHhmm(r.end))) return "Bitiş saati başlangıçtan sonra olmalı.";
  const sorted = [...ranges].sort((a, b) => parseHhmm(a.start) - parseHhmm(b.start));
  if (sorted.some((r, i) => i > 0 && parseHhmm(r.start) < parseHhmm(sorted[i - 1]!.end))) return "Aralıklar çakışıyor.";
  return null;
}

/**
 * Personelin haftalık çalışma saatleri: gün başına birden çok aralık (öğle arası için "+ Aralık ekle"), "Diğer günlere kopyala".
 * Personel işletmenin açık olduğu saatler dışında randevu almaz.
 */
export function StaffHours({ staffId, readOnly }: { staffId: string; readOnly: boolean }) {
  const refresh = useStaffRefresh();
  const hours = useQuery({
    queryKey: [...staffKey, staffId, "hours"],
    queryFn: () => unwrap(api.GET("/api/staff/{id}/working-hours", { params: { path: { id: staffId } } })),
  });
  const [draft, setDraft] = useState<StaffDay[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: (days: StaffDay[]) => unwrap(api.PUT("/api/staff/{id}/working-hours", { params: { path: { id: staffId } }, body: { days } })),
    onSuccess: async () => {
      await refresh();
      setDraft(null);
      setError(null);
      toast.success("Çalışma saatleri kaydedildi.");
    },
    onError: (e) => setError(errorMessage(e)),
  });

  if (hours.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (hours.isError) return <QueryError error={hours.error} onRetry={hours.refetch} />;

  const days = draft ?? hours.data.days;
  const update = (index: number, day: StaffDay) => setDraft(days.map((d, i) => (i === index ? day : d)));
  const copyToAll = (index: number) =>
    setDraft(days.map((d) => (d.ranges.length > 0 || d.day === days[index]!.day ? { ...d, ranges: days[index]!.ranges.map((r) => ({ ...r })) } : d)));
  const errors = days.map(dayError);

  return (
    <div className="flex flex-col gap-4">
      <ul className="flex flex-col divide-y divide-border border-y border-border">
        {days.map((day, index) => {
          const working = day.ranges.length > 0;
          return (
            <li key={day.day} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start sm:gap-4">
              <label className="flex min-h-11 w-40 shrink-0 items-center gap-3">
                <Switch
                  checked={working}
                  disabled={readOnly}
                  aria-label={`${dayNames[day.day]} çalışıyor`}
                  onCheckedChange={(on) => update(index, { ...day, ranges: on ? [{ start: "09:00", end: "18:00" }] : [] })}
                />
                <span className="text-sm font-semibold text-black">{dayNames[day.day]}</span>
              </label>
              {working ? (
                <div className="flex flex-1 flex-col gap-2">
                  {day.ranges.map((range, r) => (
                    <div key={r} className="flex items-center gap-2">
                      <Input
                        type="time"
                        aria-label={`${dayNames[day.day]} ${r + 1}. aralık başlangıç`}
                        value={range.start}
                        disabled={readOnly}
                        className="w-32"
                        onChange={(e) =>
                          update(index, { ...day, ranges: day.ranges.map((x, i) => (i === r ? { ...x, start: e.target.value } : x)) })
                        }
                      />
                      <span aria-hidden className="text-neutral-600">
                        –
                      </span>
                      <Input
                        type="time"
                        aria-label={`${dayNames[day.day]} ${r + 1}. aralık bitiş`}
                        value={range.end}
                        disabled={readOnly}
                        className="w-32"
                        onChange={(e) =>
                          update(index, { ...day, ranges: day.ranges.map((x, i) => (i === r ? { ...x, end: e.target.value } : x)) })
                        }
                      />
                      {!readOnly && day.ranges.length > 1 && (
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          aria-label={`${dayNames[day.day]} ${r + 1}. aralığı kaldır`}
                          onClick={() => update(index, { ...day, ranges: day.ranges.filter((_, i) => i !== r) })}
                        >
                          <X aria-hidden />
                        </Button>
                      )}
                    </div>
                  ))}
                  {errors[index] && (
                    <p role="alert" className="text-sm text-danger-600">
                      {errors[index]}
                    </p>
                  )}
                  {!readOnly && (
                    <div className="flex flex-wrap gap-1">
                      {day.ranges.length < MAX_RANGES && (
                        <Button
                          type="button"
                          variant="ghost"
                          onClick={() => {
                            const last = day.ranges[day.ranges.length - 1]!;
                            const start = Math.min(parseHhmm(last.end) + 60, 22 * 60);
                            const fmt = (m: number) => `${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")}`;
                            update(index, { ...day, ranges: [...day.ranges, { start: fmt(start), end: fmt(Math.min(start + 120, 23 * 60 + 59)) }] });
                          }}
                        >
                          <Plus aria-hidden />
                          Aralık ekle
                        </Button>
                      )}
                      <Button type="button" variant="ghost" onClick={() => copyToAll(index)}>
                        <Copy aria-hidden />
                        Çalıştığı diğer günlere kopyala
                      </Button>
                    </div>
                  )}
                </div>
              ) : (
                <span className="flex min-h-11 items-center text-sm text-neutral-600">Çalışmıyor</span>
              )}
            </li>
          );
        })}
      </ul>
      {error && <FormMessage>{error}</FormMessage>}
      {!readOnly && (
        <div className="flex gap-2">
          <Button onClick={() => save.mutate(days)} disabled={save.isPending || draft === null || errors.some(Boolean)}>
            Saatleri kaydet
          </Button>
          {draft && (
            <Button variant="ghost" onClick={() => setDraft(null)}>
              Vazgeç
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
