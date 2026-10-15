"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, FileDown, Printer } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { productsKey, stockKey } from "@/components/catalog/catalog-bits";
import { Notice } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { salesKey } from "@/components/sales/quick-sale";
import { TotalsSummary } from "@/components/sales/totals";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Textarea } from "@/components/ui/textarea";
import { API_URL, ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import { paymentMethodNames, type DocumentLine } from "@/lib/api/types";
import { formatDateTime, formatDayShort, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";
import { formatQuantity } from "@/lib/sales";

export function useSale(id: string) {
  return useQuery({
    queryKey: [...salesKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/sales/{id}", { params: { path: { id } } })),
  });
}

/** Kalem listesi (satış ve teklif): ad, miktar × birim fiyat, indirim, satır personeli (satışta), tutar. */
export function LineList({ lines }: { lines: DocumentLine[] }) {
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {lines.map((l) => (
        <li key={l.id} className="flex items-start justify-between gap-3 py-3">
          <span className="flex min-w-0 flex-col">
            <span className="text-sm font-semibold text-black">{l.description}</span>
            <span className="text-xs text-neutral-700 tabular-nums">
              {formatQuantity(l.quantity, l.unit)} × {formatMoney(l.unitPrice)} · KDV %{l.vatRate}
              {l.lineDiscount > 0 && ` · indirim ${formatMoney(l.lineDiscount)}`}
            </span>
            {l.staffName && <span className="text-xs text-neutral-700">Personel: {l.staffName}</span>}
          </span>
          <span className="shrink-0 text-sm font-semibold text-black tabular-nums">{formatMoney(l.lineTotal + l.allocatedDiscount)}</span>
        </li>
      ))}
    </ul>
  );
}

/** Satış detayı: kalemler, indirimler, KDV dökümü, ödemeler; fiş yazdır, PDF; Sahip/Yönetici için iptal. */
export function SaleDetail({ id }: { id: string }) {
  const { role } = useSession();
  const sale = useSale(id);
  const [cancelOpen, setCancelOpen] = useState(false);

  if (sale.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (sale.isError)
    return (
      <div className="flex flex-col gap-3">
        {sale.error instanceof ApiError && sale.error.status === 404 ? (
          <FormMessage>Satış bulunamadı.</FormMessage>
        ) : (
          <QueryError error={sale.error} onRetry={sale.refetch} />
        )}
        <Link href="/panel/satislar" className="text-sm font-semibold text-black underline">
          Satışlara dön
        </Link>
      </div>
    );

  const s = sale.data;
  const cancelled = s.status === "Cancelled";

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-5">
      <Link href="/panel/satislar" className="flex min-h-11 w-fit items-center gap-2 text-sm font-semibold text-neutral-700 hover:text-black">
        <ArrowLeft aria-hidden className="size-4" />
        Satışlar
      </Link>

      <header className="flex flex-col gap-1">
        <h1 className="flex flex-wrap items-center gap-3 text-2xl font-bold tracking-tight text-black">
          <span className="tabular-nums">{s.no}</span>
          {cancelled && <span className="rounded border-2 border-black px-2 text-base">İptal edildi</span>}
        </h1>
        <p className="text-sm text-neutral-700">
          {formatDateTime(s.date)}
          {s.createdByName && ` · ${s.createdByName}`}
        </p>
        {s.contactId && s.contactName && (
          <Link href={`/panel/musteriler/${s.contactId}`} className="w-fit text-sm font-semibold text-black underline">
            {s.contactName}
          </Link>
        )}
        {s.quoteId && s.quoteNo && (
          <Link href={`/panel/teklifler/${s.quoteId}`} className="w-fit text-sm text-black underline">
            Teklif {s.quoteNo}
          </Link>
        )}
      </header>

      {cancelled && (
        <Notice>
          {s.cancelledAt && formatDateTime(s.cancelledAt)} tarihinde {s.cancelledByName ?? ""} iptal etti.
          {s.cancelReason && ` Neden: ${s.cancelReason}`} Stok, kasa ve cari ters kayıtla geri alındı.
        </Notice>
      )}

      <div className="grid grid-cols-2 gap-2 sm:flex">
        <Button asChild variant="outline">
          <Link href={`/panel/satislar/${s.id}/fis?yazdir=1`}>
            <Printer aria-hidden />
            Fiş yazdır
          </Link>
        </Button>
        <Button asChild variant="outline">
          <a href={`${API_URL}/api/sales/${s.id}/receipt.pdf`} target="_blank" rel="noopener noreferrer">
            <FileDown aria-hidden />
            PDF
          </a>
        </Button>
      </div>

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold text-black">Kalemler</h2>
        <LineList lines={s.lines} />
        <TotalsSummary totals={s} className="sm:ml-auto sm:w-80" />
      </section>

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold text-black">Ödeme</h2>
        {s.payments.length === 0 ? (
          <p className="text-sm text-neutral-600">Ödeme yok (0 TL).</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {s.payments.map((p) => (
              <li key={p.id} className="flex min-h-11 items-center justify-between gap-3 py-2 text-sm">
                <span className="text-black">
                  {paymentMethodNames[p.method]}
                  {p.cashAccountName && <span className="text-neutral-600"> · {p.cashAccountName}</span>}
                  {p.method === "Credit" && (
                    <span className="text-neutral-600">
                      {" "}
                      · müşterinin carisine borç{s.dueDate && ` · vade ${formatDayShort(s.dueDate)}`}
                    </span>
                  )}
                </span>
                <span className="font-semibold text-black tabular-nums">{formatMoney(p.amount)}</span>
              </li>
            ))}
          </ul>
        )}
        {s.note && <p className="text-sm text-neutral-700">Not: {s.note}</p>}
      </section>

      {can(role, "cancelSales") && !cancelled && (
        <Button variant="destructive" className="self-start border border-danger-600" onClick={() => setCancelOpen(true)}>
          Satışı iptal et
        </Button>
      )}

      {cancelOpen && <CancelDialog id={s.id} no={s.no} onClose={() => setCancelOpen(false)} />}
    </div>
  );
}

function CancelDialog({ id, no, onClose }: { id: string; no: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");
  const cancel = useMutation({
    mutationFn: () => unwrap(api.POST("/api/sales/{id}/cancel", { params: { path: { id } }, body: { reason: reason.trim() || null } })),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: salesKey }),
        queryClient.invalidateQueries({ queryKey: productsKey }),
        queryClient.invalidateQueries({ queryKey: stockKey }),
        queryClient.invalidateQueries({ queryKey: ["contacts"] }),
        queryClient.invalidateQueries({ queryKey: ["cash-accounts"] }),
        queryClient.invalidateQueries({ queryKey: ["ledger"] }),
      ]);
      toast.success(`${no} iptal edildi.`);
      onClose();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{no} iptal edilsin mi?</DialogTitle>
          <DialogDescription>Stok, kasa ve cari geri alınır; satış kaydı silinmez, &quot;İptal&quot; olarak kalır.</DialogDescription>
        </DialogHeader>
        <Field label="Neden (isteğe bağlı)">
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} rows={2} />
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Vazgeç
          </Button>
          <Button variant="destructive" onClick={() => cancel.mutate()} disabled={cancel.isPending}>
            {cancel.isPending ? "İptal ediliyor…" : "Evet, iptal et"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
