"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { ArrowLeftRight, Banknote, ChevronRight, Download, HandCoins, Landmark, Pencil, Plus, ReceiptText, Undo2, Wallet } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { EmptyState, Notice, selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { AccountDialog, PaymentDialog, ReverseDialog, TransferDialog } from "@/components/finance/cash-dialogs";
import { CollectionDialog, CopyButton, cashAccountsKey, useFinanceRefresh } from "@/components/finance/finance-dialogs";
import { RangePicker, type RangeValue, resolveRange } from "@/components/finance/range-picker";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { API_URL, api, errorMessage, unwrap } from "@/lib/api/client";
import { type CashAccount, type CashTransactionItem, type CashTransactionType, cashAccountTypeNames } from "@/lib/api/types";
import { formatDayShort, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";
import { formatIban } from "@/lib/validation/rules";

export const accountHref = (id: string) => `/panel/cari-kasa/hesaplar/${id}`;

function AccountIcon({ type, className }: { type: CashAccount["type"]; className?: string }) {
  return type === "Cash" ? <Wallet aria-hidden className={cn("size-6 text-black", className)} /> : <Landmark aria-hidden className={cn("size-6 text-black", className)} />;
}

/** Bakiye: eksiyse siyah + "Eksi bakiye" etiketi (kırmızı sadece hata/silme; docs/04-tasarim.md). */
export function AccountBalance({ value, className }: { value: number; className?: string }) {
  return (
    <span className={cn("flex flex-col items-end tabular-nums", className)}>
      <span className="font-bold text-black">{formatMoney(value)}</span>
      {value < 0 && <span className="text-xs font-semibold text-neutral-700">Eksi bakiye</span>}
    </span>
  );
}

/** Hızlı finans işlemleri (kasa sekmesi ve hesap kartı): tahsilat, ödeme, transfer, gider/gelir. */
export function FinanceActions({ fromId }: { fromId?: string }) {
  const [dialog, setDialog] = useState<null | "collect" | "pay" | "transfer" | "account">(null);
  return (
    <>
      <div className="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap">
        <Button onClick={() => setDialog("collect")}>
          <HandCoins aria-hidden />
          Tahsilat al
        </Button>
        <Button variant="outline" onClick={() => setDialog("pay")}>
          <Banknote aria-hidden />
          Ödeme yap
        </Button>
        <Button variant="outline" onClick={() => setDialog("transfer")}>
          <ArrowLeftRight aria-hidden />
          Transfer
        </Button>
        <Button variant="outline" asChild>
          <Link href="/panel/cari-kasa/gelir-gider/yeni?tur=gider">
            <ReceiptText aria-hidden />
            Gider ekle
          </Link>
        </Button>
        {!fromId && (
          <Button variant="outline" onClick={() => setDialog("account")}>
            <Plus aria-hidden />
            Hesap ekle
          </Button>
        )}
      </div>
      {dialog === "collect" && <CollectionDialog open onOpenChange={(v) => !v && setDialog(null)} />}
      {dialog === "pay" && <PaymentDialog open onOpenChange={(v) => !v && setDialog(null)} />}
      {dialog === "transfer" && <TransferDialog open fromId={fromId} onOpenChange={(v) => !v && setDialog(null)} />}
      {dialog === "account" && <AccountDialog open onOpenChange={(v) => !v && setDialog(null)} />}
    </>
  );
}

/** Cari ve Kasa → Kasa ve banka: hesaplar, bakiyeler ve hızlı işlemler. */
export function CashAccountsTab() {
  const { role } = useSession();
  const [showInactive, setShowInactive] = useState(false);
  const accounts = useQuery({
    queryKey: [...cashAccountsKey, "all"],
    queryFn: () => unwrap(api.GET("/api/cash-accounts", { params: { query: { includeInactive: true } } })),
  });

  if (accounts.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (accounts.isError) return <QueryError error={accounts.error} onRetry={accounts.refetch} />;

  const active = accounts.data.filter((a) => a.isActive);
  const inactive = accounts.data.filter((a) => !a.isActive);
  const total = active.reduce((sum, a) => sum + a.balance, 0);

  return (
    <div className="flex flex-col gap-5">
      {can(role, "manageFinance") && <FinanceActions />}
      {active.length === 0 ? (
        <EmptyState icon={<Wallet className="size-10" />} title="Kasa hesabı yok" />
      ) : (
        <section aria-label="Hesaplar" className="flex flex-col gap-3">
          <h2 className="flex items-baseline justify-between text-lg font-semibold text-black">
            Hesaplar
            <span className="text-base tabular-nums">Toplam {formatMoney(total)}</span>
          </h2>
          <AccountList accounts={active} />
        </section>
      )}
      {inactive.length > 0 && (
        <section className="flex flex-col gap-3">
          <Button variant="ghost" className="self-start" aria-expanded={showInactive} onClick={() => setShowInactive((v) => !v)}>
            Pasif hesaplar ({inactive.length})
          </Button>
          {showInactive && <AccountList accounts={inactive} />}
        </section>
      )}
    </div>
  );
}

function AccountList({ accounts }: { accounts: CashAccount[] }) {
  return (
    <ul className="grid gap-3 sm:grid-cols-2">
      {accounts.map((a) => (
        <li key={a.id}>
          <Link href={accountHref(a.id)} className="flex min-h-16 items-center gap-3 border border-border px-4 py-3 hover:border-black">
            <AccountIcon type={a.type} />
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="flex flex-wrap items-center gap-2">
                <span className="truncate text-sm font-semibold text-black">{a.name}</span>
                {a.isDefault && <Badge variant="outline">Varsayılan</Badge>}
                {!a.isActive && <Badge variant="outline">Pasif</Badge>}
              </span>
              <span className="truncate text-xs text-neutral-600">
                {cashAccountTypeNames[a.type]}
                {a.iban && ` · ${formatIban(a.iban).slice(0, 9)}…${a.iban.slice(-4)}`}
              </span>
            </span>
            <AccountBalance value={a.balance} className="text-lg" />
            <ChevronRight aria-hidden className="size-4 shrink-0 text-neutral-500" />
          </Link>
        </li>
      ))}
    </ul>
  );
}

const typeFilters: { key: "" | CashTransactionType; label: string }[] = [
  { key: "", label: "Tüm hareketler" },
  { key: "SaleIncome", label: "Satış" },
  { key: "Collection", label: "Tahsilat" },
  { key: "Payment", label: "Ödeme" },
  { key: "Expense", label: "Gider" },
  { key: "Income", label: "Gelir" },
  { key: "TransferIn", label: "Gelen transfer" },
  { key: "TransferOut", label: "Giden transfer" },
];

/** Hesap kartı: bakiye, IBAN, tarih filtreli hareketler (devreden, giriş/çıkış toplamı, yürüyen bakiye), ters kayıt, Excel. */
export function AccountDetail({ id }: { id: string }) {
  const { role } = useSession();
  const manage = can(role, "manageFinance");
  const refresh = useFinanceRefresh();
  const [range, setRange] = useState<RangeValue>({ preset: "bu-ay" });
  const [type, setType] = useState<"" | CashTransactionType>("");
  const [dialog, setDialog] = useState<null | "edit">(null);
  const [reversing, setReversing] = useState<CashTransactionItem | null>(null);
  const { from, to } = resolveRange(range);

  const data = useQuery({
    queryKey: ["cash-transactions", id, from, to, type],
    queryFn: () =>
      unwrap(api.GET("/api/cash-accounts/{id}/transactions", { params: { path: { id }, query: { from, to, type: type || undefined } } })),
  });

  const action = useMutation({
    mutationFn: (kind: "default" | "deactivate" | "activate") =>
      unwrap(
        kind === "default"
          ? api.POST("/api/cash-accounts/{id}/default", { params: { path: { id } } })
          : kind === "deactivate"
            ? api.POST("/api/cash-accounts/{id}/deactivate", { params: { path: { id } } })
            : api.POST("/api/cash-accounts/{id}/activate", { params: { path: { id } } }),
      ),
    onSuccess: async (_, kind) => {
      toast.success(kind === "default" ? "Varsayılan hesap değişti." : kind === "deactivate" ? "Hesap pasife alındı." : "Hesap tekrar açıldı.");
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (data.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (data.isError) return <QueryError error={data.error} onRetry={data.refetch} />;

  const { account } = data.data;
  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-col gap-3 border-b border-border pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex items-start gap-3">
          <AccountIcon type={account.type} className="mt-1 size-7" />
          <div className="flex min-w-0 flex-col gap-1">
            <h1 className="text-2xl font-bold tracking-tight text-black">{account.name}</h1>
            <span className="flex flex-wrap gap-2 text-sm text-neutral-600">
              {cashAccountTypeNames[account.type]}
              {account.isDefault && <Badge variant="outline">Varsayılan</Badge>}
              {!account.isActive && <Badge variant="outline">Pasif</Badge>}
            </span>
          </div>
        </div>
        <div className="flex flex-col items-start sm:items-end">
          <span className="text-sm text-neutral-600">Güncel bakiye</span>
          <AccountBalance value={account.balance} className="items-start text-3xl sm:items-end" />
        </div>
      </header>

      {account.iban && (
        <section aria-label="Banka bilgileri" className="flex flex-col gap-2">
          <p className="text-sm text-black tabular-nums">
            <span className="text-neutral-600">IBAN </span>
            {formatIban(account.iban)}
          </p>
          {account.ibanHolderName && <p className="text-sm text-black">Hesap sahibi: {account.ibanHolderName}</p>}
          <div className="flex flex-wrap gap-2">
            <CopyButton text={account.iban} label="IBAN'ı kopyala" />
          </div>
        </section>
      )}

      {manage && account.isActive && <FinanceActions fromId={account.id} />}
      {manage && (
        <div className="flex flex-wrap gap-2">
          <Button variant="ghost" onClick={() => setDialog("edit")}>
            <Pencil aria-hidden />
            Düzenle
          </Button>
          {account.isActive && !account.isDefault && (
            <Button variant="ghost" onClick={() => action.mutate("default")} disabled={action.isPending}>
              Varsayılan yap
            </Button>
          )}
          {account.isActive && !account.isDefault && (
            <Button
              variant="ghost"
              disabled={action.isPending}
              onClick={() => window.confirm(`${account.name} pasife alınsın mı? Hareketleri korunur.`) && action.mutate("deactivate")}
            >
              Pasife al
            </Button>
          )}
          {!account.isActive && (
            <Button variant="outline" onClick={() => action.mutate("activate")} disabled={action.isPending}>
              Tekrar aç
            </Button>
          )}
        </div>
      )}

      <section aria-label="Hareketler" className="flex flex-col gap-4">
        <h2 className="text-lg font-semibold text-black">Hareketler</h2>
        <RangePicker value={range} onChange={setRange} />
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <label className="flex max-w-xs flex-col gap-1 text-sm font-semibold text-black">
            Tür
            <select className={selectClass} value={type} onChange={(e) => setType(e.target.value as typeof type)}>
              {typeFilters.map((t) => (
                <option key={t.key} value={t.key}>
                  {t.label}
                </option>
              ))}
            </select>
          </label>
          {can(role, "exportData") && (
            <Button variant="outline" asChild>
              <a href={`${API_URL}/api/cash-accounts/${id}/transactions.xlsx?from=${from}&to=${to}`} download>
                <Download aria-hidden />
                {"Excel'e aktar"}
              </a>
            </Button>
          )}
        </div>
        <dl className="grid grid-cols-2 gap-3 border-y border-border py-3 text-sm sm:grid-cols-4">
          {[
            ["Devreden", data.data.openingBalance],
            ["Giriş", data.data.inTotal],
            ["Çıkış", data.data.outTotal],
            ["Dönem sonu", data.data.closingBalance],
          ].map(([label, value]) => (
            <div key={label as string} className="flex flex-col">
              <dt className="text-neutral-600">{label}</dt>
              <dd className="font-semibold text-black tabular-nums">{formatMoney(value as number)}</dd>
            </div>
          ))}
        </dl>
        {data.data.items.length === 0 ? (
          <p className="text-sm text-neutral-600">Bu aralıkta hareket yok.</p>
        ) : (
          <TransactionRows items={data.data.items} onReverse={setReversing} />
        )}
      </section>

      {dialog === "edit" && <AccountDialog account={account} open onOpenChange={(v) => !v && setDialog(null)} />}
      {reversing && <ReverseDialog item={reversing} open onOpenChange={(v) => !v && setReversing(null)} />}
    </div>
  );
}

function ReversalNote({ item }: { item: CashTransactionItem }) {
  if (item.reversalOf) return <span className="text-xs text-neutral-700">Ters kayıt · {formatDayShort(item.reversalOf.date)} tarihli harekete</span>;
  if (item.reversedBy)
    return <span className="text-xs text-neutral-700">Ters kaydı yapıldı · {formatDayShort(item.reversedBy.date)}</span>;
  return null;
}

/** Hareket listesi: masaüstünde tablo, telefonda kart. Ters kayıt ve geri alınan hareket birbirine bağlı görünür. */
function TransactionRows({ items, onReverse }: { items: CashTransactionItem[]; onReverse: (i: CashTransactionItem) => void }) {
  const rows = [...items].reverse(); // en yeni üstte
  const detail = (i: CashTransactionItem) =>
    [i.typeLabel, i.categoryName, i.contactName, i.counterAccountName && `Karşı hesap: ${i.counterAccountName}`, i.documentNo && `Belge ${i.documentNo}`]
      .filter(Boolean)
      .join(" · ");
  const reverseButton = (i: CashTransactionItem) =>
    i.canReverse && (
      <Button variant="ghost" size="icon" aria-label="Ters kayıt" title="Ters kayıt" onClick={() => onReverse(i)}>
        <Undo2 aria-hidden />
      </Button>
    );
  return (
    <>
      <table className="hidden w-full text-sm md:table">
        <thead>
          <tr className="border-b border-black text-left text-neutral-700">
            <th className="py-2 font-semibold">Tarih</th>
            <th className="py-2 font-semibold">Açıklama</th>
            <th className="py-2 text-right font-semibold">Giriş</th>
            <th className="py-2 text-right font-semibold">Çıkış</th>
            <th className="py-2 text-right font-semibold">Bakiye</th>
            <th className="w-12 py-2">
              <span className="sr-only">İşlem</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((i) => (
            <tr key={i.id} className={cn("border-b border-border align-top", (i.isReversal || i.reversedBy) && "text-neutral-700")}>
              <td className="py-2 tabular-nums">{formatDayShort(i.date)}</td>
              <td className="py-2">
                <span className="flex flex-col">
                  <span className="font-semibold text-black">{i.description}</span>
                  <span className="text-xs text-neutral-600">{detail(i)}</span>
                  <ReversalNote item={i} />
                </span>
              </td>
              <td className="py-2 text-right tabular-nums">{i.netAmount > 0 ? formatMoney(i.netAmount) : ""}</td>
              <td className="py-2 text-right tabular-nums">{i.netAmount < 0 ? formatMoney(-i.netAmount) : ""}</td>
              <td className="py-2 text-right font-semibold tabular-nums">{formatMoney(i.balance)}</td>
              <td className="py-1 text-right">{reverseButton(i)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <ul className="flex flex-col divide-y divide-border border-y border-border md:hidden">
        {rows.map((i) => (
          <li key={i.id} className="flex items-start gap-3 py-3">
            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
              <span className="text-sm font-semibold text-black">{i.description}</span>
              <span className="text-xs text-neutral-600">
                {formatDayShort(i.date)} · {detail(i)}
              </span>
              <ReversalNote item={i} />
            </span>
            <span className="flex flex-col items-end text-right tabular-nums">
              <span className="text-sm font-semibold text-black">
                {i.netAmount > 0 ? "+" : "−"}
                {formatMoney(Math.abs(i.netAmount))}
              </span>
              <span className="text-xs text-neutral-600">{formatMoney(i.balance)}</span>
            </span>
            {reverseButton(i)}
          </li>
        ))}
      </ul>
      {rows.some((i) => i.isReversal) && <Notice>Ters kayıtlar giriş ve çıkış toplamından düşülmüştür.</Notice>}
    </>
  );
}
