"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { productsKey } from "@/components/catalog/catalog-bits";
import { QueryError } from "@/components/feedback/query-error";
import { FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import type { Staff } from "@/lib/api/types";

import { useStaffRefresh } from "./staff-bits";

/** Aktif hizmetler (randevu formu, personel hizmetleri). */
export function useServices() {
  return useQuery({
    queryKey: [...productsKey, "services"],
    queryFn: () => unwrap(api.GET("/api/products", { params: { query: { type: "Service", pageSize: 100 } } })),
    select: (data) => data.items,
  });
}

/** Personelin verebildiği hizmetler; hiçbiri seçili değilse bütün hizmetler. */
export function StaffServices({ staff, readOnly }: { staff: Staff; readOnly: boolean }) {
  const refresh = useStaffRefresh();
  const services = useServices();
  const [selected, setSelected] = useState<string[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const current = selected ?? staff.serviceIds;
  const save = useMutation({
    mutationFn: (productIds: string[]) =>
      unwrap(api.PUT("/api/staff/{id}/services", { params: { path: { id: staff.id } }, body: { productIds } })),
    onSuccess: async () => {
      await refresh();
      setSelected(null);
      toast.success("Hizmetler kaydedildi.");
    },
    onError: (e) => setError(errorMessage(e)),
  });

  if (services.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (services.isError) return <QueryError error={services.error} onRetry={services.refetch} />;
  if (services.data.length === 0)
    return (
      <p className="text-sm text-neutral-600">
        Henüz hizmet yok.{" "}
        <Link href="/panel/urunler/yeni" className="font-semibold text-black underline">
          Hizmet ekleyin
        </Link>{" "}
        (tür: Hizmet, süre dakika olarak).
      </p>
    );

  const toggle = (id: string, on: boolean) => setSelected(on ? [...current, id] : current.filter((x) => x !== id));

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-neutral-600">
        {current.length === 0 ? "Hiçbiri seçili değil: personel bütün hizmetleri verir." : `${current.length} hizmet seçili; sadece bunlara randevu verilir.`}
      </p>
      <ul className="flex flex-col divide-y divide-border border-y border-border">
        {services.data.map((s) => (
          <li key={s.id}>
            <label className="flex min-h-12 items-center gap-3 py-1">
              <Checkbox checked={current.includes(s.id)} disabled={readOnly} onCheckedChange={(v) => toggle(s.id, v === true)} />
              <span className="flex-1 text-sm text-black">{s.name}</span>
              <span className="text-sm text-neutral-600 tabular-nums">{s.durationMinutes ? `${s.durationMinutes} dk` : "Süre yok"}</span>
            </label>
          </li>
        ))}
      </ul>
      {error && <FormMessage>{error}</FormMessage>}
      {!readOnly && (
        <div className="flex gap-2">
          <Button onClick={() => save.mutate(current)} disabled={save.isPending || selected === null}>
            Hizmetleri kaydet
          </Button>
          {current.length > 0 && (
            <Button variant="ghost" onClick={() => setSelected([])}>
              Hepsini verir
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
