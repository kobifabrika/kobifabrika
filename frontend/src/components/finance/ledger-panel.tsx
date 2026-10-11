"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { Download, FileText, HandCoins, Link2, Plus, Undo2, X } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";

import { Balance } from "@/components/contacts/contact-bits";
import {
  CollectionDialog,
  LedgerEntryDialog,
  PaymentLinkDialog,
  StatementDialog,
  useFinanceRefresh,
} from "@/components/finance/finance-dialogs";
import { QueryError } from "@/components/feedback/query-error";
import { PaymentRequestList } from "@/components/finance/payment-request-list";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { API_URL, api, errorMessage, unwrap } from "@/lib/api/client";
import type { Contact, LedgerItem } from "@/lib/api/types";
import { formatDayShort, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";

const refLabels: Record<LedgerItem["refType"], string> = {
  Opening: "Açılış",
  Manual: "",
  Collection: "Tahsilat",
  Sale: "Satış",
  SaleCancel: "Satış iptali",
};

/** Kartın Cari sekmesi: bakiye, işlemler, hareketler (yürüyen bakiye), tahsilat linkleri. */
export function LedgerPanel({ contact }: { contact: Contact }) {
  const { role } = useSession();
  const manage = can(role, "manageFinance");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [dialog, setDialog] = useState<null | "entry" | "collect" | "statement" | "link">(null);

  const ledger = useQuery({
    queryKey: ["ledger", contact.id, from, to],
    queryFn: () =>
      unwrap(
        api.GET("/api/contacts/{id}/ledger", {
          params: { path: { id: contact.id }, query: { from: from || undefined, to: to || undefined } },
        }),
      ),
  });

  const balance = contact.balance ?? 0;
  const query = new URLSearchParams({ ...(from && { from }), ...(to && { to }) }).toString();

  return (
    <div className="flex flex-col gap-6">
      <section className="flex flex-col gap-4 border-b border-border pb-5">
        <div className="flex flex-col gap-1">
          <span className="text-sm text-neutral-600">Güncel bakiye</span>
          <Balance value={balance} className="text-2xl font-bold" />
          <span className="text-xs text-neutral-600">
            Alacak: müşteri size borçlu · Borç: siz müşteriye borçlusunuz
          </span>
        </div>
        <div className="flex flex-wrap gap-2">
          {manage && (
            <>
              <Button onClick={() => setDialog("collect")}>
                <HandCoins aria-hidden />
                Tahsilat al
              </Button>
              <Button variant="outline" onClick={() => setDialog("entry")}>
                <Plus aria-hidden />
                Borç / alacak
              </Button>
              <Button variant="outline" onClick={() => setDialog("link")}>
                <Link2 aria-hidden />
                Tahsilat linki
              </Button>
            </>
          )}
          <Button variant="outline" onClick={() => setDialog("statement")}>
            <FileText aria-hidden />
            Ekstre
          </Button>
          {can(role, "exportData") && (
            <Button asChild variant="ghost">
              <a href={`${API_URL}/api/contacts/${contact.id}/ledger.xlsx${query ? `?${query}` : ""}`} download>
                <Download aria-hidden />
                Excel
              </a>
            </Button>
          )}
        </div>
      </section>

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <h2 className="text-lg font-semibold text-black">Hareketler</h2>
          <div className="flex items-end gap-2">
            <label className="flex flex-col gap-1 text-xs font-semibold text-neutral-700">
              Başlangıç
              <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="w-40" />
            </label>
            <label className="flex flex-col gap-1 text-xs font-semibold text-neutral-700">
              Bitiş
              <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="w-40" />
            </label>
            {(from || to) && (
              <Button
                variant="ghost"
                size="icon"
                aria-label="Tarih aralığını temizle"
                onClick={() => {
                  setFrom("");
                  setTo("");
                }}
              >
                <X aria-hidden />
              </Button>
            )}
          </div>
        </div>

        {ledger.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : ledger.isError ? (
          <QueryError error={ledger.error} onRetry={ledger.refetch} />
        ) : ledger.data.items.length === 0 && ledger.data.carriedBalance === 0 ? (
          <p className="text-sm text-neutral-600">Bu aralıkta hareket yok.</p>
        ) : (
          <LedgerRows
            items={ledger.data.items}
            carried={ledger.data.from ? ledger.data.carriedBalance : null}
            manage={manage}
          />
        )}
        {ledger.data && ledger.data.items.length > 0 && (
          <p className="flex flex-wrap gap-x-4 text-sm text-neutral-700 tabular-nums">
            <span>Toplam borç {formatMoney(ledger.data.totalDebit)}</span>
            <span>Toplam alacak {formatMoney(ledger.data.totalCredit)}</span>
          </p>
        )}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold text-black">Tahsilat linkleri</h2>
        <PaymentRequestList contactId={contact.id} showContact={false} />
      </section>

      {dialog === "entry" && <LedgerEntryDialog contact={contact} open onOpenChange={(v) => !v && setDialog(null)} />}
      {dialog === "collect" && <CollectionDialog contact={contact} open onOpenChange={(v) => !v && setDialog(null)} />}
      {dialog === "statement" && (
        <StatementDialog contact={contact} open canShare={manage} onOpenChange={(v) => !v && setDialog(null)} />
      )}
      {dialog === "link" && <PaymentLinkDialog contact={contact} open onOpenChange={(v) => !v && setDialog(null)} />}
    </div>
  );
}

function LedgerRows({ items, carried, manage }: { items: LedgerItem[]; carried: number | null; manage: boolean }) {
  const refresh = useFinanceRefresh();
  const remove = useMutation({
    mutationFn: (item: LedgerItem) =>
      unwrap(
        item.refType === "Collection"
          ? api.DELETE("/api/collections/{cashTransactionId}", { params: { path: { cashTransactionId: item.refId! } } })
          : api.DELETE("/api/ledger-entries/{entryId}", { params: { path: { entryId: item.id } } }),
      ),
    onSuccess: async (_, item) => {
      toast.success(item.refType === "Collection" ? "Tahsilat geri alındı." : "Hareket silindi.");
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const confirmRemove = (item: LedgerItem) => {
    const text =
      item.refType === "Collection" ? "Bu tahsilat kasadan ve cariden geri alınsın mı?" : "Bu hareket silinsin mi?";
    if (window.confirm(text)) remove.mutate(item);
  };

  const removable = (i: LedgerItem) =>
    manage && (i.refType === "Manual" || i.refType === "Opening" || (i.refType === "Collection" && i.refId));
  const rows = [...items].reverse(); // en yeni üstte

  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {rows.map((i) => (
        <li key={i.id} className="flex items-start gap-3 py-3">
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <span className="text-sm font-semibold text-black">{i.description}</span>
            <span className="text-xs text-neutral-600">
              {formatDayShort(i.date)}
              {refLabels[i.refType] && ` · ${refLabels[i.refType]}`}
              {i.dueDate && ` · Vade ${formatDayShort(i.dueDate)}`}
            </span>
          </div>
          <div className="flex flex-col items-end gap-0.5 text-right tabular-nums">
            <span className="text-sm font-semibold text-black">
              {i.debit > 0 ? `Borç ${formatMoney(i.debit)}` : `Alacak ${formatMoney(i.credit)}`}
            </span>
            <Balance value={i.balance} className="text-xs" />
          </div>
          {removable(i) && (
            <Button
              variant="ghost"
              size="icon"
              aria-label={i.refType === "Collection" ? "Tahsilatı geri al" : "Hareketi sil"}
              title={i.refType === "Collection" ? "Tahsilatı geri al" : "Sil"}
              onClick={() => confirmRemove(i)}
              disabled={remove.isPending}
            >
              {i.refType === "Collection" ? <Undo2 aria-hidden /> : <X aria-hidden />}
            </Button>
          )}
        </li>
      ))}
      {carried !== null && (
        <li className="flex items-center justify-between py-3 text-sm text-neutral-700">
          <span>Devreden bakiye</span>
          <Balance value={carried} className="text-sm" />
        </li>
      )}
    </ul>
  );
}
