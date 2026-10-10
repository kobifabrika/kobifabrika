"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { Link2 } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { EmptyState } from "@/components/contacts/contact-bits";
import {
  ConfirmPaymentDialog,
  paymentRequestsKey,
  SharePaymentDialog,
  useFinanceRefresh,
} from "@/components/finance/finance-dialogs";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import { paymentStatusNames, type PaymentRequest } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";

/** Tahsilat linkleri; "Ödedim" diyenler en üstte. contactId verilirse sadece o müşterinin linkleri. */
export function PaymentRequestList({ contactId, showContact = true }: { contactId?: string; showContact?: boolean }) {
  const requests = useQuery({
    queryKey: [...paymentRequestsKey, contactId ?? "all"],
    queryFn: () => unwrap(api.GET("/api/payment-requests", { params: { query: { contactId } } })),
  });

  if (requests.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (requests.isError)
    return (
      <p role="alert" className="text-sm text-danger-600">
        {errorMessage(requests.error)}
      </p>
    );
  if (requests.data.length === 0) {
    return contactId ? (
      <p className="text-sm text-neutral-600">Bu müşteriye henüz tahsilat linki gönderilmedi.</p>
    ) : (
      <EmptyState icon={<Link2 className="size-10" />} title="Henüz tahsilat linki yok">
        <p className="text-sm text-neutral-600">
          Müşteri kartındaki Cari sekmesinden &quot;Tahsilat linki&quot; ile gönderebilirsiniz.
        </p>
      </EmptyState>
    );
  }

  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {requests.data.map((r) => (
        <PaymentRequestRow key={r.id} request={r} showContact={showContact} />
      ))}
    </ul>
  );
}

function statusText(r: PaymentRequest) {
  if (r.isExpired && (r.status === "Open" || r.status === "ClaimedPaid")) return "Süresi doldu";
  return paymentStatusNames[r.status];
}

function PaymentRequestRow({ request: r, showContact }: { request: PaymentRequest; showContact: boolean }) {
  const { role } = useSession();
  const refresh = useFinanceRefresh();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);
  const manage = can(role, "manageFinance");
  const open = r.status === "Open" || r.status === "ClaimedPaid";
  const claimed = r.status === "ClaimedPaid";

  const action = useMutation({
    mutationFn: (kind: "reject" | "cancel") =>
      unwrap(
        kind === "reject"
          ? api.POST("/api/payment-requests/{id}/reject", { params: { path: { id: r.id } } })
          : api.POST("/api/payment-requests/{id}/cancel", { params: { path: { id: r.id } } }),
      ),
    onSuccess: async (_, kind) => {
      toast.success(kind === "reject" ? "Link tekrar ödeme bekliyor." : "Link iptal edildi.");
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <li
      className={cn("flex flex-col gap-3 py-3 sm:flex-row sm:items-center", claimed && "border-l-2 border-brand pl-3")}
    >
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="flex flex-wrap items-baseline gap-x-2">
          {showContact && (
            <Link href={`/panel/musteriler/${r.contactId}`} className="font-semibold text-black hover:underline">
              {r.contactName}
            </Link>
          )}
          <span className="font-semibold text-black tabular-nums">{formatMoney(r.amount)}</span>
          <span className={cn("text-xs font-semibold", claimed ? "text-black" : "text-neutral-600")}>
            {statusText(r)}
          </span>
        </span>
        <span className="text-xs text-neutral-600">
          {formatDateTime(r.createdAt)} gönderildi
          {r.claimedAt && ` · ${formatDateTime(r.claimedAt)} "Ödedim" dedi`}
          {r.confirmedAt && ` · ${formatDateTime(r.confirmedAt)} onaylandı`}
        </span>
        {r.note && <span className="text-sm text-neutral-700">{r.note}</span>}
        {r.claimNote && <span className="text-sm text-black">Müşteri notu: {r.claimNote}</span>}
      </div>
      {manage && open && (
        <div className="flex flex-wrap gap-2">
          <Button size="default" onClick={() => setConfirmOpen(true)}>
            Onayla
          </Button>
          {claimed && (
            <Button variant="outline" onClick={() => action.mutate("reject")} disabled={action.isPending}>
              Ödeme gelmedi
            </Button>
          )}
          <Button variant="outline" onClick={() => setShareOpen(true)}>
            Paylaş
          </Button>
          <Button variant="ghost" onClick={() => action.mutate("cancel")} disabled={action.isPending}>
            İptal
          </Button>
        </div>
      )}
      {confirmOpen && <ConfirmPaymentDialog request={r} open={confirmOpen} onOpenChange={setConfirmOpen} />}
      {shareOpen && <SharePaymentDialog request={r} open={shareOpen} onOpenChange={setShareOpen} />}
    </li>
  );
}
