"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { useDeferredValue, useState } from "react";
import { toast } from "sonner";

import { QueryError } from "@/components/feedback/query-error";
import { Field } from "@/components/forms/field";
import { smsCreditKey } from "@/components/marketing/sms-credit";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { AdminBusinessItem } from "@/lib/api/types";

const number = (n: number) => n.toLocaleString("tr-TR");

/**
 * Kredi yükleme (sadece KobiFabrika ekibi, PlatformAdmin): işletme ara → kredi miktarı → not → Yükle. Ödeme Sprint 8'de
 * (iyzico); şimdilik işletme destekten ister, ekip buradan yükler. Tam süper admin paneli Sprint 8'de.
 */
export function PlatformCredit() {
  const { me } = useSession();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const term = useDeferredValue(search.trim());
  const [selected, setSelected] = useState<AdminBusinessItem | null>(null);
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const businesses = useQuery({
    queryKey: ["admin", "businesses", term],
    enabled: me.user.isPlatformAdmin,
    queryFn: () => unwrap(api.GET("/api/admin/businesses", { params: { query: { search: term || undefined } } })),
  });
  const topUp = useMutation({
    mutationFn: (business: AdminBusinessItem) =>
      unwrap(
        api.POST("/api/admin/businesses/{id}/sms-credits", {
          params: { path: { id: business.id } },
          body: { amount: Number(amount), note: note.trim() },
        }),
      ),
    onSuccess: async (result) => {
      toast.success(`${result.businessName}: ${number(Number(amount))} kredi yüklendi. Yeni bakiye ${number(result.balance)}.`);
      setSelected((s) => (s ? { ...s, smsBalance: result.balance } : s));
      setAmount("");
      setNote("");
      setErrors({});
      await queryClient.invalidateQueries({ queryKey: ["admin", "businesses"] });
      await queryClient.invalidateQueries({ queryKey: smsCreditKey });
    },
    onError: (e) => {
      setErrors(fieldErrors(e));
      toast.error(errorMessage(e));
    },
  });

  if (!me.user.isPlatformAdmin) return <p className="text-sm text-neutral-600">Bu sayfa sadece KobiFabrika ekibine açık.</p>;

  return (
    <div className="flex flex-col gap-5 lg:max-w-2xl">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-bold tracking-tight text-black">Kredi yükle (platform)</h1>
        <p className="text-sm text-neutral-600">Sadece KobiFabrika ekibi görür. Yükleme işletmenin kredi hareketlerinde ve aktivite günlüğünde görünür.</p>
      </div>

      <div className="relative">
        <Search aria-hidden className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600" />
        <Input
          type="search"
          aria-label="İşletme ara"
          placeholder="İşletme adı, link veya sahip e-postası"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="pl-9"
        />
      </div>

      {businesses.isError ? (
        <QueryError error={businesses.error} onRetry={businesses.refetch} />
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border" aria-label="İşletmeler">
          {(businesses.data ?? []).map((b) => (
            <li key={b.id}>
              <button
                type="button"
                aria-pressed={selected?.id === b.id}
                onClick={() => setSelected(b)}
                className={
                  "flex min-h-14 w-full items-center justify-between gap-3 px-3 py-2 text-left " +
                  (selected?.id === b.id ? "bg-black text-white" : "text-black hover:bg-neutral-50")
                }
              >
                <span className="flex min-w-0 flex-col">
                  <span className="truncate text-sm font-semibold">{b.name}</span>
                  <span className={"truncate text-xs " + (selected?.id === b.id ? "text-neutral-300" : "text-neutral-600")}>
                    {b.ownerEmail ?? b.slug}
                  </span>
                </span>
                <span className="shrink-0 text-sm tabular-nums">{number(b.smsBalance)} kredi</span>
              </button>
            </li>
          ))}
          {businesses.data?.length === 0 && <li className="px-3 py-3 text-sm text-neutral-600">İşletme bulunamadı.</li>}
        </ul>
      )}

      {selected && (
        <form
          className="flex flex-col gap-4 border border-black px-4 py-4"
          onSubmit={(e) => {
            e.preventDefault();
            topUp.mutate(selected);
          }}
        >
          <p className="text-sm text-black">
            <strong>{selected.name}</strong> · bakiye {number(selected.smsBalance)} kredi
          </p>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Kredi miktarı" error={errors.amount}>
              <Input inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value.replace(/\D/g, ""))} />
            </Field>
            <Field label="Not" hint="Ör. ödeme açıklaması" error={errors.note}>
              <Input value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
            </Field>
          </div>
          <Button type="submit" className="self-start max-sm:w-full" disabled={topUp.isPending || !amount || !note.trim()}>
            Yükle
          </Button>
        </form>
      )}
    </div>
  );
}
