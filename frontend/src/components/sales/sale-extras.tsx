"use client";

import { selectClass } from "@/components/contacts/contact-bits";
import { useCashAccountOptions } from "@/components/finance/cash-dialogs";
import { Field } from "@/components/forms/field";
import { Input } from "@/components/ui/input";
import type { PaymentDraft } from "@/lib/cart";

export type SaleExtrasState = { dueDate: string; cashAccountId: string; bankAccountId: string };

export const initialSaleExtras: SaleExtrasState = { dueDate: "", cashAccountId: "", bankAccountId: "" };

/**
 * Ödemeye bağlı ek alanlar: veresiye varsa vade tarihi (varsayılan bugün + Ayarlar'daki gün); birden fazla aktif kasa veya
 * banka hesabı varsa paranın gireceği hesap (varsayılan, Ayarlar → Finans'taki varsayılan hesap).
 */
export function SaleExtras({
  drafts,
  value,
  onChange,
  defaultDueDate,
  today,
  dueError,
}: {
  drafts: PaymentDraft[];
  value: SaleExtrasState;
  onChange: (v: SaleExtrasState) => void;
  defaultDueDate: string;
  today: string;
  dueError?: string | null;
}) {
  const needsCash = drafts.some((d) => d.method === "Cash");
  const needsBank = drafts.some((d) => d.method === "Card" || d.method === "Transfer");
  const credit = drafts.some((d) => d.method === "Credit");
  const options = useCashAccountOptions(needsCash || needsBank);
  const cash = (options.data ?? []).filter((a) => a.type === "Cash");
  const bank = (options.data ?? []).filter((a) => a.type === "Bank");
  const showCash = needsCash && cash.length > 1;
  const showBank = needsBank && bank.length > 1;
  if (!credit && !showCash && !showBank) return null;

  return (
    <div className="flex flex-col gap-3">
      {credit && (
        <Field label="Vade" error={dueError ?? undefined} hint="Veresiye kısmının ödeneceği tarih">
          <Input type="date" min={today} value={value.dueDate || defaultDueDate} onChange={(e) => onChange({ ...value, dueDate: e.target.value })} />
        </Field>
      )}
      {showCash && (
        <Field label="Nakit hangi kasaya?">
          <select
            className={selectClass}
            value={value.cashAccountId || cash.find((a) => a.isDefault)?.id || ""}
            onChange={(e) => onChange({ ...value, cashAccountId: e.target.value })}
          >
            {cash.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </Field>
      )}
      {showBank && (
        <Field label="Kart/havale hangi hesaba?">
          <select
            className={selectClass}
            value={value.bankAccountId || bank.find((a) => a.isDefault)?.id || ""}
            onChange={(e) => onChange({ ...value, bankAccountId: e.target.value })}
          >
            {bank.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </Field>
      )}
    </div>
  );
}
