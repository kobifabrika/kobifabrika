"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";

import { invalidDays, WorkingHoursEditor } from "@/components/business/working-hours-editor";
import { QueryError } from "@/components/feedback/query-error";
import { FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import type { WorkingHour } from "@/lib/api/types";
import { can } from "@/lib/permissions";

const key = ["business", "working-hours"] as const;

export function WorkingHoursSettings() {
  const hours = useQuery({ queryKey: key, queryFn: () => unwrap(api.GET("/api/business/working-hours")) });

  if (hours.isPending) return <p role="status" className="text-sm text-neutral-600">Yükleniyor…</p>;
  if (hours.isError) return <QueryError error={hours.error} onRetry={hours.refetch} />;
  return <Editor initial={hours.data} />;
}

function Editor({ initial }: { initial: WorkingHour[] }) {
  const { role } = useSession();
  const readOnly = !can(role, "manageBusiness");
  const queryClient = useQueryClient();
  const [value, setValue] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dirty = JSON.stringify(value) !== JSON.stringify(initial);

  const save = async () => {
    const invalid = invalidDays(value);
    if (invalid.length) return setError(`${invalid.join(", ")}: kapanış saati açılış saatinden sonra olmalı.`);
    setBusy(true);
    setError(null);
    try {
      const saved = await unwrap(api.PUT("/api/business/working-hours", { body: { days: value } }));
      queryClient.setQueryData(key, saved);
      setValue(saved);
      toast.success("Çalışma saatleri kaydedildi.");
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-neutral-600">Online randevu ve takvim bu saatlere göre çalışır. Saatler İstanbul saatidir.</p>
      <WorkingHoursEditor value={value} onChange={setValue} disabled={readOnly} />
      {error && <FormMessage>{error}</FormMessage>}
      {!readOnly && (
        <Button size="lg" className="self-start" disabled={busy || !dirty} onClick={save}>
          {busy ? "Kaydediliyor…" : "Kaydet"}
        </Button>
      )}
    </div>
  );
}
