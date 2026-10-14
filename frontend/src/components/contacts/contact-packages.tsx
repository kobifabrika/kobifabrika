"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Layers } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { useContactPackages } from "@/components/appointments/appointment-form";
import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { packageStateNames, type CustomerPackage } from "@/lib/api/types";
import { formatDay, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";

/** Müşteri kartı → Paketler: kalan/toplam seans, son gün, durum; Sahip/Yönetici süreyi uzatır veya paketi kapatır. */
export function ContactPackageList({ contactId }: { contactId: string }) {
  const packages = useContactPackages(contactId);
  if (packages.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (packages.isError) return <QueryError error={packages.error} onRetry={packages.refetch} />;
  if (packages.data.items.length === 0)
    return (
      <EmptyState icon={<Layers className="size-8" />} title="Paket yok">
        <p className="text-sm text-neutral-600">Hızlı satıştan paket satınca burada görünür; seanslar randevu tamamlanınca düşer.</p>
      </EmptyState>
    );
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {packages.data.items.map((p) => (
        <PackageRow key={p.id} pkg={p} contactId={contactId} />
      ))}
    </ul>
  );
}

function PackageRow({ pkg: p, contactId }: { pkg: CustomerPackage; contactId: string }) {
  const { role } = useSession();
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<"extend" | "close" | null>(null);
  const [date, setDate] = useState(p.expiresOn);
  const [note, setNote] = useState("");
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["contacts", "packages", contactId] });
  const extend = useMutation({
    mutationFn: () => unwrap(api.POST("/api/customer-packages/{id}/extend", { params: { path: { id: p.id } }, body: { expiresOn: date } })),
    onSuccess: async () => {
      await refresh();
      setMode(null);
      toast.success("Paketin süresi uzatıldı.");
    },
  });
  const close = useMutation({
    mutationFn: () => unwrap(api.POST("/api/customer-packages/{id}/close", { params: { path: { id: p.id } }, body: { note: note.trim() } })),
    onSuccess: async () => {
      await refresh();
      setMode(null);
      toast.success("Paket kapatıldı.");
    },
  });
  const manage = can(role, "manageCatalog") && (p.state === "Active" || p.state === "Expired");
  const error = extend.error ?? close.error;

  return (
    <li className="flex flex-col gap-2 py-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col">
          <span className="text-base font-semibold text-black">{p.name}</span>
          <span className="text-sm text-neutral-700 tabular-nums">
            {p.usedSessions}/{p.totalSessions} seans kullanıldı · kalan {p.remaining}
          </span>
          <span className="text-xs text-neutral-600">
            {p.serviceName} · son gün {formatDay(p.expiresOn)}
            {p.saleNo && (
              <>
                {" · "}
                <Link href={`/panel/satislar/${p.saleId}`} className="underline">
                  {p.saleNo}
                </Link>
              </>
            )}
          </span>
          {p.closeNote && <span className="text-xs text-neutral-600">Not: {p.closeNote}</span>}
        </div>
        <span className="rounded-md border border-black px-2 py-0.5 text-xs font-semibold text-black">{packageStateNames[p.state]}</span>
      </div>
      {manage && mode === null && (
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={() => setMode("extend")}>
            Süreyi uzat
          </Button>
          <Button variant="ghost" size="sm" className="text-danger-600" onClick={() => setMode("close")}>
            Paketi kapat
          </Button>
        </div>
      )}
      {mode === "extend" && (
        <div className="flex flex-col gap-2 border border-border p-3">
          <Field label="Yeni son gün">
            <Input type="date" min={todayIstanbul()} value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <div className="flex gap-2">
            <Button size="sm" onClick={() => extend.mutate()} disabled={extend.isPending || !date}>
              Kaydet
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setMode(null)}>
              Vazgeç
            </Button>
          </div>
        </div>
      )}
      {mode === "close" && (
        <div className="flex flex-col gap-2 border border-border p-3">
          <Field label="Kapatma nedeni" hint="Kalan seanslar düşer. Paketli satışı iptal etmeden önce paket kapatılır.">
            <Textarea rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
          <div className="flex gap-2">
            <Button size="sm" variant="destructive" onClick={() => close.mutate()} disabled={close.isPending || !note.trim()}>
              Paketi kapat
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setMode(null)}>
              Vazgeç
            </Button>
          </div>
        </div>
      )}
      {error && <FormMessage>{errorMessage(error)}</FormMessage>}
    </li>
  );
}
