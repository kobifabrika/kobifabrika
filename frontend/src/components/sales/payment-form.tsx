"use client";

import { cn } from "cn";
import { Banknote, BookUser, CreditCard, Landmark, Plus, Trash2 } from "lucide-react";
import type { LucideIcon } from "lucide-react";

import { selectClass } from "@/components/contacts/contact-bits";
import { Button } from "@/components/ui/button";
import { NumberInput } from "@/components/ui/number-input";
import { paymentMethodNames, type PaymentMethod } from "@/lib/api/types";
import { changeDue, remaining, type PaymentDraft } from "@/lib/cart";
import { formatMoney } from "@/lib/format";
import { moneyToInput, parseMoneyInput } from "@/lib/validation/rules";

export type PaymentState =
  | { mode: "single"; method: PaymentMethod | null; received: string }
  | { mode: "split"; rows: { method: PaymentMethod; text: string }[] };

export const initialPayment = (method: PaymentMethod | null = null): PaymentState => ({ mode: "single", method, received: "" });

const methods: { method: PaymentMethod; icon: LucideIcon }[] = [
  { method: "Cash", icon: Banknote },
  { method: "Card", icon: CreditCard },
  { method: "Transfer", icon: Landmark },
  { method: "Credit", icon: BookUser },
];

const amountOf = (text: string) => parseMoneyInput(text)?.value ?? 0;

/** Formdaki ödemeler: tek tipte tutar her zaman satış toplamıdır. */
export function paymentDrafts(state: PaymentState, total: number): PaymentDraft[] {
  if (total === 0) return [];
  if (state.mode === "single") return state.method ? [{ method: state.method, amount: total }] : [];
  return state.rows.map((r) => ({ method: r.method, amount: amountOf(r.text) }));
}

/**
 * Ödeme: Nakit, Kart, Havale/EFT, Veresiye. Tek dokunuşla tamamı seçilir; "Böl" ile birden fazla tip ve tutar
 * (kalan gösterilir). Nakitte "Alınan" girilirse para üstü hesaplanır (kayıt edilen tutar satış tutarıdır).
 * Veresiye kısmı müşterinin carisine borç yazılır; nakit kasaya, kart ve havale bankaya işlenir.
 */
export function PaymentForm({
  total,
  value,
  onChange,
  disabledMethods = [],
}: {
  total: number;
  value: PaymentState;
  onChange: (state: PaymentState) => void;
  disabledMethods?: PaymentMethod[];
}) {
  if (total === 0) return <p className="text-sm text-neutral-700">Tutar 0 olduğu için ödeme alınmaz.</p>;

  if (value.mode === "split") {
    const drafts = paymentDrafts(value, total);
    const left = remaining(total, drafts);
    const setRow = (i: number, patch: Partial<{ method: PaymentMethod; text: string }>) =>
      onChange({ mode: "split", rows: value.rows.map((r, j) => (j === i ? { ...r, ...patch } : r)) });
    return (
      <div className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <span className="text-sm font-semibold text-black">Bölünmüş ödeme</span>
          <Button type="button" variant="ghost" onClick={() => onChange(initialPayment())}>
            Tek ödeme
          </Button>
        </div>
        {value.rows.map((row, i) => (
          <div key={i} className="flex gap-2">
            <select
              aria-label={`${i + 1}. ödeme tipi`}
              className={cn(selectClass, "w-36 shrink-0")}
              value={row.method}
              onChange={(e) => setRow(i, { method: e.target.value as PaymentMethod })}
            >
              {methods
                .filter((m) => !disabledMethods.includes(m.method))
                .map((m) => (
                  <option key={m.method} value={m.method}>
                    {paymentMethodNames[m.method]}
                  </option>
                ))}
            </select>
            <NumberInput
              aria-label={`${i + 1}. ödeme tutarı`}
              placeholder="0,00"
              value={row.text}
              onChange={(e) => setRow(i, { text: e.target.value })}
            />
            <Button
              type="button"
              variant="outline"
              size="icon"
              aria-label={`${i + 1}. ödemeyi sil`}
              disabled={value.rows.length <= 1}
              onClick={() => onChange({ mode: "split", rows: value.rows.filter((_, j) => j !== i) })}
            >
              <Trash2 aria-hidden />
            </Button>
          </div>
        ))}
        <div className="flex items-center justify-between gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={value.rows.length >= 8}
            onClick={() =>
              onChange({
                mode: "split",
                rows: [...value.rows, { method: "Card", text: left > 0 ? moneyToInput(left) : "" }],
              })
            }
          >
            <Plus aria-hidden />
            Ödeme ekle
          </Button>
          <span className={cn("text-sm tabular-nums", left === 0 ? "text-neutral-700" : "font-semibold text-black")}>
            {left === 0 ? "✓ Tamamı ödendi" : left > 0 ? `Kalan ${formatMoney(left)}` : `Fazla ${formatMoney(-left)}`}
          </span>
        </div>
      </div>
    );
  }

  const change = value.method === "Cash" ? changeDue(total, value.received.trim() ? amountOf(value.received) : null) : 0;
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-semibold text-black">Ödeme</span>
        <Button
          type="button"
          variant="ghost"
          onClick={() =>
            onChange({
              mode: "split",
              rows: [
                { method: value.method ?? "Cash", text: "" },
                { method: value.method === "Credit" ? "Cash" : "Credit", text: "" },
              ],
            })
          }
        >
          Böl
        </Button>
      </div>
      <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Ödeme tipi">
        {methods
          .filter((m) => !disabledMethods.includes(m.method))
          .map(({ method, icon: Icon }) => (
            <button
              key={method}
              type="button"
              role="radio"
              aria-checked={value.method === method}
              onClick={() => onChange({ ...value, method })}
              className={cn(
                "flex min-h-14 items-center justify-center gap-2 rounded-lg border text-sm font-semibold",
                value.method === method ? "border-link bg-link text-white" : "border-border bg-background text-black hover:border-black",
              )}
            >
              <Icon aria-hidden className="size-5" />
              {paymentMethodNames[method]}
            </button>
          ))}
      </div>
      {value.method === "Cash" && (
        <div className="flex items-end gap-3">
          <label className="flex flex-1 flex-col gap-2 text-sm font-semibold text-black">
            Alınan (isteğe bağlı)
            <NumberInput
              placeholder={moneyToInput(total)}
              value={value.received}
              onChange={(e) => onChange({ ...value, received: e.target.value })}
            />
          </label>
          <div className="flex min-h-11 flex-col justify-end pb-1 text-right">
            <span className="text-xs text-neutral-600">Para üstü</span>
            <span className="text-lg font-bold text-black tabular-nums">{formatMoney(change)}</span>
          </div>
        </div>
      )}
    </div>
  );
}
