"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Wallet } from "lucide-react";
import Link from "next/link";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { ChoiceGroup } from "@/components/catalog/catalog-bits";
import { QueryError } from "@/components/feedback/query-error";
import { FormMessage } from "@/components/forms/field";
import { MoneyField } from "@/components/finance/finance-dialogs";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { salaryBasisNames, type SalaryBasis, type StaffSalary } from "@/lib/api/types";
import { formatDayShort, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";
import { moneyToInput, parseMoneyInput, validateMoney } from "@/lib/validation/rules";

import { staffKey } from "./staff-bits";

const salaryKey = (id: string) => [...staffKey, id, "salary"] as const;

/**
 * Maaş sekmesi (Sahip düzenler, Mali Müşavir salt okunur görür; Yönetici ve Personel bu sekmeyi görmez, API maaşı hiç döndürmez).
 * Bilgi amaçlı aylık tutar; bordro hesabı yok. Altında bu personele bağlı maaş giderleri.
 */
export function StaffSalaryTab({ staffId, staffName }: { staffId: string; staffName: string }) {
  const { role } = useSession();
  const manage = can(role, "manageSalary");
  const salary = useQuery({
    queryKey: salaryKey(staffId),
    queryFn: () => unwrap(api.GET("/api/staff/{id}/salary", { params: { path: { id: staffId } } })),
  });
  const payments = useQuery({
    queryKey: [...salaryKey(staffId), "payments"],
    queryFn: () => unwrap(api.GET("/api/staff/{id}/salary-payments", { params: { path: { id: staffId } } })),
  });

  return (
    <div className="flex max-w-2xl flex-col gap-6">
      <section className="flex flex-col gap-3">
        <h2 className="text-base font-semibold text-black">Aylık maaş</h2>
        {salary.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : salary.isError ? (
          <QueryError error={salary.error} onRetry={salary.refetch} />
        ) : manage ? (
          <SalaryForm key={`${salary.data.salary}-${salary.data.basis}`} staffId={staffId} current={salary.data} />
        ) : (
          <p className="text-2xl font-bold text-black tabular-nums">
            {salary.data.salary == null ? "Girilmemiş" : `${formatMoney(salary.data.salary)} · ${salaryBasisNames[salary.data.basis]}`}
          </p>
        )}
        <p className="text-sm text-neutral-600">Bilgi amaçlıdır; bordro hesabı yapılmaz. Maaşı sadece işletme sahibi ve mali müşavir görür.</p>
      </section>

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-base font-semibold text-black">Ödenen maaşlar</h2>
          {manage && (
            <Button asChild variant="outline" className="max-sm:w-full">
              <Link href={`/panel/cari-kasa/gelir-gider/yeni?tur=gider&personel=${staffId}`}>
                <Wallet aria-hidden />
                Maaş öde
              </Link>
            </Button>
          )}
        </div>
        {payments.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : payments.isError ? (
          <QueryError error={payments.error} onRetry={payments.refetch} />
        ) : payments.data.items.length === 0 ? (
          <p className="text-sm text-neutral-600">
            {staffName} adına kayıtlı maaş ödemesi yok. Maaş giderini girerken personeli seçin.
          </p>
        ) : (
          <>
            <ul className="flex flex-col divide-y divide-border border-y border-border">
              {payments.data.items.map((p) => (
                <li key={p.id} className="flex min-h-14 flex-wrap items-center justify-between gap-x-4 gap-y-1 py-2">
                  <span className="flex min-w-0 flex-col">
                    <span className="flex flex-wrap items-center gap-2 text-sm font-semibold text-black">
                      {p.description}
                      {p.isReversal && <Badge variant="outline">Ters kayıt</Badge>}
                      {p.isReversed && <Badge variant="outline">Ters kaydı yapıldı</Badge>}
                    </span>
                    <span className="text-sm text-neutral-600 tabular-nums">
                      {formatDayShort(p.date)} · {p.cashAccountName}
                      {p.documentNo ? ` · ${p.documentNo}` : ""}
                    </span>
                  </span>
                  <span className={`text-sm font-semibold tabular-nums ${p.isReversed && !p.isReversal ? "text-neutral-600 line-through" : "text-black"}`}>
                    {formatMoney(p.netAmount)}
                  </span>
                </li>
              ))}
            </ul>
            <p className="self-end text-sm font-semibold text-black tabular-nums">Toplam {formatMoney(payments.data.total)}</p>
          </>
        )}
      </section>
    </div>
  );
}

function SalaryForm({ staffId, current }: { staffId: string; current: StaffSalary }) {
  const queryClient = useQueryClient();
  const [amount, setAmount] = useState(current.salary == null ? "" : moneyToInput(current.salary));
  const [basis, setBasis] = useState<SalaryBasis>(current.basis);
  const [submitted, setSubmitted] = useState(false);
  const amountError = submitted && amount.trim() ? validateMoney(amount) : null;

  const save = useMutation({
    mutationFn: (body: StaffSalary) => unwrap(api.PUT("/api/staff/{id}/salary", { params: { path: { id: staffId } }, body })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: salaryKey(staffId) });
      toast.success("Maaş kaydedildi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    if (amount.trim() && validateMoney(amount)) return;
    save.mutate({ salary: amount.trim() ? parseMoneyInput(amount)!.value : null, basis });
  };

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-4">
      <div className="max-w-xs">
        <MoneyField label="Tutar (aylık, boş: girilmemiş)" value={amount} onChange={setAmount} error={amountError} />
      </div>
      <ChoiceGroup
        label="Brüt veya net"
        options={(["Net", "Gross"] as SalaryBasis[]).map((b) => ({ value: b, label: salaryBasisNames[b] }))}
        value={basis}
        onChange={setBasis}
      />
      {save.isError && <FormMessage>{errorMessage(save.error)}</FormMessage>}
      <Button type="submit" className="self-start" disabled={save.isPending}>
        Kaydet
      </Button>
    </form>
  );
}
