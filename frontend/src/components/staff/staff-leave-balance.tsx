"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { SlidersHorizontal } from "lucide-react";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { ResponsivePanel } from "@/components/appointments/appointment-bits";
import { ChoiceGroup } from "@/components/catalog/catalog-bits";
import { Notice } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { leaveBalanceKindNames, type LeaveBalance, type LeaveBalanceKind } from "@/lib/api/types";
import { formatDate, formatDayShort } from "@/lib/format";
import { dateRangeText, daysText } from "@/lib/time-clock";

export const leaveBalanceKey = (staffId: string) => ["leave", "balance", staffId] as const;

/** Yıllık izin bakiyesi: büyük rakam, yasal öneri (İş Kanunu 53), yasal not, hareketler; yönetici açılış/düzeltme girer. */
export function LeaveBalanceCard({ staffId, manage }: { staffId: string; manage: boolean }) {
  const [adjusting, setAdjusting] = useState(false);
  const balance = useQuery({
    queryKey: leaveBalanceKey(staffId),
    queryFn: () => unwrap(api.GET("/api/staff/{id}/leave-balance", { params: { path: { id: staffId } } })),
  });

  if (balance.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (balance.isError) return <QueryError error={balance.error} onRetry={balance.refetch} />;
  const b = balance.data;

  return (
    <div className="flex max-w-2xl flex-col gap-5">
      <section className="flex flex-wrap items-end justify-between gap-4 border-b border-border pb-4">
        <div className="flex flex-col gap-1">
          <span className="text-sm text-neutral-600">Yıllık izin bakiyesi</span>
          <span className="text-3xl font-bold text-black tabular-nums" data-testid="leave-balance">
            {daysText(b.balance)}
          </span>
          {b.balance < 0 && <span className="text-sm text-neutral-600">Eksi bakiye: avans izin kullanılmış.</span>}
        </div>
        {manage && (
          <Button variant="outline" onClick={() => setAdjusting(true)} className="max-sm:w-full">
            <SlidersHorizontal aria-hidden />
            Bakiyeyi düzelt
          </Button>
        )}
      </section>

      <EntitlementInfo balance={b} manage={manage} />
      <Notice>{b.legalNote}</Notice>

      <section className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-black">Hareketler</h3>
        {b.entries.length === 0 ? (
          <p className="text-sm text-neutral-600">Henüz hareket yok.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {b.entries.map((e) => (
              <li key={e.id} className="flex min-h-12 items-center justify-between gap-3 py-2">
                <span className="flex min-w-0 flex-col">
                  <span className="text-sm font-semibold text-black">{leaveBalanceKindNames[e.kind]}</span>
                  <span className="text-sm text-neutral-600">
                    {e.startDate && e.endDate
                      ? dateRangeText(e.startDate, e.endDate)
                      : e.note && e.note !== leaveBalanceKindNames[e.kind]
                        ? `${e.note} · ${formatDate(e.createdAt)}`
                        : formatDate(e.createdAt)}
                  </span>
                </span>
                <span className="shrink-0 text-sm font-semibold text-black tabular-nums">
                  {e.days > 0 ? "+" : ""}
                  {daysText(e.days)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
      {manage && <AdjustDialog staffId={staffId} open={adjusting} onOpenChange={setAdjusting} hasEntries={b.entries.length > 0} />}
    </div>
  );
}

function EntitlementInfo({ balance, manage }: { balance: LeaveBalance; manage: boolean }) {
  const info = balance.entitlement;
  if (!info)
    return (
      <p className="text-sm text-neutral-600">
        İşe başlama tarihi girilmemiş; yıllık izin hakkı önerilemez.{manage ? " Bilgiler sekmesinden ekleyin." : ""}
      </p>
    );
  return (
    <ul className="flex flex-col gap-1 text-sm text-black">
      <li>
        İşe başlama: <span className="tabular-nums">{formatDayShort(balance.startDate!)}</span> · Kıdem {info.completedYears} yıl
      </li>
      {info.completedYears > 0 && <li>İş Kanunu 53. maddeye göre bu yılki hak: {info.currentYearDays} gün</li>}
      <li>
        Sonraki hak: <span className="tabular-nums">{formatDayShort(info.nextAnniversary)}</span> tarihinde {info.nextYearDays} gün (otomatik eklenir)
      </li>
      {balance.entries.length === 0 && info.completedYears > 0 && manage && (
        <li className="text-neutral-600">Geçmiş yıllar otomatik yazılmaz; kalan izni açılış bakiyesi olarak girin.</li>
      )}
    </ul>
  );
}

function AdjustDialog({
  staffId,
  open,
  onOpenChange,
  hasEntries,
}: {
  staffId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  hasEntries: boolean;
}) {
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<LeaveBalanceKind>(hasEntries ? "Adjustment" : "Opening");
  const [days, setDays] = useState("");
  const [note, setNote] = useState(hasEntries ? "" : "Açılış bakiyesi");
  const [errors, setErrors] = useState<Record<string, string>>({});

  const save = useMutation({
    mutationFn: (body: { kind: LeaveBalanceKind; days: number; note: string }) =>
      unwrap(api.POST("/api/staff/{id}/leave-balance/adjustments", { params: { path: { id: staffId } }, body })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: leaveBalanceKey(staffId) });
      toast.success("Bakiye güncellendi.");
      setDays("");
      onOpenChange(false);
    },
    onError: (e) => setErrors(Object.keys(fieldErrors(e)).length ? fieldErrors(e) : { form: errorMessage(e) }),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const value = Number(days.replace(",", "."));
    const next: Record<string, string> = {};
    if (!days.trim() || !Number.isFinite(value) || value === 0 || value * 2 !== Math.floor(value * 2))
      next.days = "Gün sayısını yarım gün adımlarıyla girin (ör. 14 veya 2,5; düşmek için eksi).";
    if (!note.trim()) next.note = "Açıklama girin.";
    setErrors(next);
    if (Object.keys(next).length === 0) save.mutate({ kind, days: value, note: note.trim() });
  };

  return (
    <ResponsivePanel open={open} onOpenChange={onOpenChange} title="Bakiyeyi düzelt" description="Eksi değer bakiyeden düşer.">
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <ChoiceGroup
          label="Tür"
          options={(["Opening", "Adjustment"] as LeaveBalanceKind[]).map((k) => ({ value: k, label: leaveBalanceKindNames[k] }))}
          value={kind}
          onChange={setKind}
        />
        <Field label="Gün" error={errors.days}>
          <Input inputMode="decimal" value={days} onChange={(e) => setDays(e.target.value)} placeholder="14" />
        </Field>
        <Field label="Açıklama" error={errors.note}>
          <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} />
        </Field>
        {errors.form && <FormMessage>{errors.form}</FormMessage>}
        <Button type="submit" disabled={save.isPending}>
          Kaydet
        </Button>
      </form>
    </ResponsivePanel>
  );
}
