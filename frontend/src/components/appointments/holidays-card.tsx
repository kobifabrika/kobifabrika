"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { formatDay, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";

import { calendarKey } from "./appointment-bits";

const holidaysKey = ["holidays"] as const;

/** Ayarlar → Çalışma saatleri: tatil ve kapalı günler (elle eklenir). O gün takvimde kapalı, online randevu alınmaz. */
export function HolidaysCard() {
  const { role } = useSession();
  const manage = can(role, "manageStaff");
  const queryClient = useQueryClient();
  const holidays = useQuery({ queryKey: holidaysKey, queryFn: () => unwrap(api.GET("/api/holidays")) });
  const [date, setDate] = useState("");
  const [name, setName] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const refresh = () =>
    Promise.all([queryClient.invalidateQueries({ queryKey: holidaysKey }), queryClient.invalidateQueries({ queryKey: calendarKey })]);

  const add = useMutation({
    mutationFn: () => unwrap(api.POST("/api/holidays", { body: { date, name: name.trim() } })),
    onSuccess: async () => {
      await refresh();
      setDate("");
      setName("");
      toast.success("Kapalı gün eklendi.");
    },
    onError: (e) => {
      setErrors(fieldErrors(e));
      setError(Object.keys(fieldErrors(e)).length ? null : errorMessage(e));
    },
  });
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/holidays/{id}", { params: { path: { id } } })),
    onSuccess: async () => {
      await refresh();
      toast.success("Kapalı gün silindi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (!date) next.date = "Tarih seçin.";
    if (!name.trim()) next.name = "Adını girin (ör. Kurban Bayramı).";
    setErrors(next);
    setError(null);
    if (!Object.keys(next).length) add.mutate();
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Tatil ve kapalı günler</CardTitle>
        <CardDescription>Bu günlerde takvim kapalı görünür ve online randevu alınmaz. Resmi tatilleri ve izin günlerinizi ekleyin.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {manage && (
          <form onSubmit={submit} noValidate className="flex flex-wrap items-end gap-3">
            <Field label="Tarih" error={errors.date} className="w-44">
              <Input type="date" value={date} min={todayIstanbul()} onChange={(e) => setDate(e.target.value)} />
            </Field>
            <Field label="Adı" error={errors.name} className="min-w-48 flex-1">
              <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} placeholder="Ör. Kurban Bayramı 1. gün" />
            </Field>
            <Button type="submit" variant="outline" disabled={add.isPending}>
              Kapalı gün ekle
            </Button>
          </form>
        )}
        {error && <FormMessage>{error}</FormMessage>}
        {holidays.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : holidays.isError ? (
          <QueryError error={holidays.error} onRetry={holidays.refetch} />
        ) : holidays.data.length === 0 ? (
          <p className="text-sm text-neutral-600">Önümüzdeki bir yıl için kapalı gün yok.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {holidays.data.map((h) => (
              <li key={h.id} className="flex min-h-12 items-center gap-2 py-1">
                <span className="w-40 text-sm font-semibold text-black tabular-nums">{formatDay(h.date)}</span>
                <span className="flex-1 text-sm text-neutral-700">{h.name}</span>
                {manage && (
                  <Button variant="ghost" size="icon" aria-label={`${formatDay(h.date)} kapalı gününü sil`} onClick={() => remove.mutate(h.id)}>
                    <Trash2 aria-hidden />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
