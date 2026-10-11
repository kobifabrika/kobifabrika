"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Check, FileDown, MoreVertical, Pencil, Send, X } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";

import { productsKey, stockKey } from "@/components/catalog/catalog-bits";
import { Notice } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { DialogShell, ShareButtons } from "@/components/finance/finance-dialogs";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { QuoteStatusBadge, quotesKey, useQuotesRefresh } from "@/components/quotes/quote-bits";
import { PaymentForm, initialPayment, paymentDrafts, type PaymentState } from "@/components/sales/payment-form";
import { salesKey } from "@/components/sales/quick-sale";
import { LineList } from "@/components/sales/sale-detail";
import { TotalsSummary } from "@/components/sales/totals";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Textarea } from "@/components/ui/textarea";
import { API_URL, ApiError, api, errorMessage, problemCode, unwrap } from "@/lib/api/client";
import type { Quote, QuoteStatus } from "@/lib/api/types";
import { paymentError } from "@/lib/cart";
import { formatDateTime, formatDay, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";

const steps: { status: QuoteStatus; label: string }[] = [
  { status: "Draft", label: "Taslak" },
  { status: "Sent", label: "Gönderildi" },
  { status: "Accepted", label: "Onaylandı" },
  { status: "Converted", label: "Satışa dönüştü" },
];

/** Durum çizgisi: Taslak → Gönderildi → Onaylandı (veya Reddedildi) → Satışa dönüştü. Geçilen adımlar mavi çizgili. */
function StatusLine({ status }: { status: QuoteStatus }) {
  const order: Record<QuoteStatus, number> = { Draft: 0, Sent: 1, Accepted: 2, Rejected: 2, Converted: 3 };
  const current = order[status];
  return (
    <ol className="flex gap-2 text-xs font-semibold" aria-label="Teklif durumu">
      {steps.map((s, i) => {
        const label = i === 2 && status === "Rejected" ? "Reddedildi" : s.label;
        return (
          <li
            key={s.status}
            aria-current={i === current ? "step" : undefined}
            className={i <= current ? "flex-1 border-t-2 border-brand pt-2 text-black" : "flex-1 border-t-2 border-neutral-200 pt-2 text-neutral-600"}
          >
            {label}
          </li>
        );
      })}
    </ol>
  );
}

export function QuoteDetail({ id }: { id: string }) {
  const { role } = useSession();
  const router = useRouter();
  const refresh = useQuotesRefresh();
  const [shareOpen, setShareOpen] = useState(false);
  const [convertOpen, setConvertOpen] = useState(false);
  const [respond, setRespond] = useState<"Accepted" | "Rejected" | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const quote = useQuery({
    queryKey: [...quotesKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/quotes/{id}", { params: { path: { id } } })),
  });

  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/quotes/{id}", { params: { path: { id } } })),
    onSuccess: async () => {
      await refresh();
      toast.success("Teklif silindi; müşterideki link artık açılmaz.");
      router.push("/panel/satislar?sekme=teklifler");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (quote.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (quote.isError)
    return (
      <div className="flex flex-col gap-3">
        {quote.error instanceof ApiError && quote.error.status === 404 ? (
          <FormMessage>Teklif bulunamadı.</FormMessage>
        ) : (
          <QueryError error={quote.error} onRetry={quote.refetch} />
        )}
        <Link href="/panel/satislar?sekme=teklifler" className="text-sm font-semibold text-black underline">
          Tekliflere dön
        </Link>
      </div>
    );

  const q = quote.data;
  const write = can(role, "createSales");
  const editable = q.status === "Draft" || q.status === "Sent";

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-5">
      <Link
        href="/panel/satislar?sekme=teklifler"
        className="flex min-h-11 w-fit items-center gap-2 text-sm font-semibold text-neutral-700 hover:text-black"
      >
        <ArrowLeft aria-hidden className="size-4" />
        Teklifler
      </Link>

      <header className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="flex flex-wrap items-center gap-3 text-2xl font-bold tracking-tight text-black">
            <span className="tabular-nums">{q.no}</span>
            <QuoteStatusBadge status={q.status} expired={q.isExpired} className="text-sm" />
          </h1>
          <Link href={`/panel/musteriler/${q.contactId}`} className="w-fit text-sm font-semibold text-black underline">
            {q.contactName}
          </Link>
          <p className="text-sm text-neutral-700">
            {formatDay(q.date)} · {formatDay(q.validUntil)} tarihine kadar geçerli
            {q.createdByName && ` · ${q.createdByName}`}
          </p>
          <p className="text-2xl font-bold text-black tabular-nums">{formatMoney(q.total)}</p>
        </div>
        {write && (editable || q.status !== "Converted") && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="icon" aria-label="Diğer işlemler">
                <MoreVertical aria-hidden />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              {editable && (
                <>
                  <DropdownMenuItem className="min-h-11" onSelect={() => setRespond("Accepted")}>
                    Müşteri onayladı
                  </DropdownMenuItem>
                  <DropdownMenuItem className="min-h-11" onSelect={() => setRespond("Rejected")}>
                    Müşteri reddetti
                  </DropdownMenuItem>
                </>
              )}
              <DropdownMenuItem className="min-h-11 text-danger-600" onSelect={() => setConfirmDelete(true)}>
                Sil
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </header>

      <StatusLine status={q.status} />

      {q.isExpired && (
        <Notice>Geçerlilik tarihi geçti; müşteriye gönderilen link açılmaz. &quot;Paylaş&quot; ile geçerliliği uzatıp yeni link oluşturabilirsiniz.</Notice>
      )}
      {q.respondedAt && (q.status === "Accepted" || q.status === "Rejected" || q.status === "Converted") && (
        <Notice>
          {formatDateTime(q.respondedAt)} · {q.respondedVia === "Link" ? "Müşteri linkten" : "Panelden"}{" "}
          {q.status === "Rejected" ? "reddetti" : "onayladı"}.
          {q.responseNote && ` Not: "${q.responseNote}"`}
        </Notice>
      )}
      {q.status === "Converted" && q.saleId && (
        <Link href={`/panel/satislar/${q.saleId}`} className="w-fit text-sm font-semibold text-black underline">
          Satış {q.saleNo}
        </Link>
      )}

      <div className="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap">
        {q.status === "Accepted" && write && (
          <Button className="col-span-2" onClick={() => setConvertOpen(true)}>
            <Check aria-hidden />
            Satışa dönüştür
          </Button>
        )}
        {write && q.status !== "Converted" && (
          <Button variant={q.status === "Draft" ? "default" : "outline"} onClick={() => setShareOpen(true)}>
            <Send aria-hidden />
            Paylaş
          </Button>
        )}
        <Button asChild variant="outline">
          <a href={`${API_URL}/api/quotes/${q.id}/pdf`} target="_blank" rel="noopener noreferrer">
            <FileDown aria-hidden />
            PDF
          </a>
        </Button>
        {write && editable && (
          <Button asChild variant="outline">
            <Link href={`/panel/teklifler/${q.id}/duzenle`}>
              <Pencil aria-hidden />
              Düzenle
            </Link>
          </Button>
        )}
      </div>

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold text-black">Kalemler</h2>
        <LineList lines={q.lines} />
        <TotalsSummary totals={q} className="sm:ml-auto sm:w-80" />
        {q.notes && <p className="text-sm whitespace-pre-line text-neutral-700">{q.notes}</p>}
        {q.lastViewedAt && <p className="text-xs text-neutral-600">Müşteri en son {formatDateTime(q.lastViewedAt)} tarihinde açtı.</p>}
      </section>

      {shareOpen && <ShareQuoteDialog quote={q} onClose={() => setShareOpen(false)} />}
      {convertOpen && <ConvertDialog quote={q} onClose={() => setConvertOpen(false)} />}
      {respond && <RespondDialog quote={q} status={respond} onClose={() => setRespond(null)} />}
      <Dialog open={confirmDelete} onOpenChange={setConfirmDelete}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{q.no} silinsin mi?</DialogTitle>
            <DialogDescription>Müşteriye gönderilen link artık açılmaz.</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmDelete(false)}>
              Vazgeç
            </Button>
            <Button variant="destructive" onClick={() => remove.mutate()} disabled={remove.isPending}>
              Sil
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

type ShareAction = { action: "share" | "renew"; sendEmail: boolean; extend?: boolean };

/**
 * Paylaş: geçerli link aynen gösterilir (müşteriye daha önce gönderilen çalışmaya devam eder); "Linki yenile" eskisini kapatır.
 * Süresi dolmuş teklifte ikisi de önce "15 gün uzatılsın mı?" diye sorar; fiyatlar aynı kalır.
 */
function ShareQuoteDialog({ quote, onClose }: { quote: Quote; onClose: () => void }) {
  const refresh = useQuotesRefresh();
  const [link, setLink] = useState<{ url: string; whatsappUrl: string; isNewLink: boolean; validUntil: string } | null>(null);
  const [confirmRenew, setConfirmRenew] = useState(false);
  const [askExtend, setAskExtend] = useState<"share" | "renew" | null>(quote.isExpired ? "share" : null);
  const share = useMutation({
    mutationFn: ({ action, sendEmail, extend = false }: ShareAction) =>
      unwrap(
        action === "share"
          ? api.POST("/api/quotes/{id}/share", { params: { path: { id: quote.id } }, body: { sendEmail, extend } })
          : api.POST("/api/quotes/{id}/renew", { params: { path: { id: quote.id } }, body: { sendEmail, extend } }),
      ),
    onSuccess: async (r, { action, sendEmail, extend }) => {
      setLink({ url: r.url, whatsappUrl: r.whatsappUrl, isNewLink: r.isNewLink, validUntil: r.quote.validUntil });
      setConfirmRenew(false);
      setAskExtend(null);
      if (extend) toast.success(`Geçerlilik ${formatDay(r.quote.validUntil)} tarihine uzatıldı; yeni link oluşturuldu.`);
      else if (action === "renew") toast.success("Link yenilendi; eski link artık çalışmıyor.");
      if (sendEmail) toast[r.emailSent ? "success" : "error"](r.emailSent ? "E-posta gönderildi." : "E-posta gönderilemedi.");
      await refresh();
    },
    onError: (e, { action }) => {
      if (problemCode(e) === "quote_expired") {
        setLink(null);
        setConfirmRenew(false);
        setAskExtend(action);
        return;
      }
      toast.error(errorMessage(e));
    },
  });
  const requested = useRef(false);
  const { mutate } = share;
  useEffect(() => {
    if (!requested.current && !quote.isExpired) {
      requested.current = true;
      mutate({ action: "share", sendEmail: false });
    }
  }, [mutate, quote.isExpired]);

  if (askExtend) {
    return (
      <DialogShell open onOpenChange={(v) => !v && onClose()} title="Teklifi paylaş" description={`${quote.contactName} · ${formatMoney(quote.total)}`}>
        <div className="flex flex-col gap-4">
          <Notice>Teklifin süresi doldu. Geçerliliği 15 gün uzatıp yeni link oluşturulsun mu?</Notice>
          <p className="text-sm text-neutral-600">Fiyatlar aynı kalır. Müşteriye daha önce gönderilen link çalışmaz.</p>
          <div className="grid grid-cols-2 gap-2">
            <Button variant="outline" onClick={onClose} disabled={share.isPending}>
              Vazgeç
            </Button>
            <Button onClick={() => share.mutate({ action: askExtend, sendEmail: false, extend: true })} disabled={share.isPending}>
              {share.isPending ? "Uzatılıyor…" : "Evet, uzat"}
            </Button>
          </div>
        </div>
      </DialogShell>
    );
  }

  return (
    <DialogShell open onOpenChange={(v) => !v && onClose()} title="Teklifi paylaş" description={`${quote.contactName} · ${formatMoney(quote.total)}`}>
      {link ? (
        <div className="flex flex-col gap-4">
          <ShareButtons
            url={link.url}
            whatsappUrl={link.whatsappUrl}
            onEmail={() => share.mutate({ action: "share", sendEmail: true })}
            emailDisabled={quote.contactEmail ? null : "Müşterinin e-posta adresi yok."}
            emailBusy={share.isPending && share.variables?.sendEmail}
          />
          <p className="text-sm text-neutral-600">
            {formatDay(link.validUntil)} tarihine kadar geçerli.
            {!link.isNewLink && " Müşteriye daha önce gönderilen link aynen geçerli."}
          </p>
          {quote.status === "Draft" || quote.status === "Sent" ? (
            confirmRenew ? (
              <div className="flex flex-col gap-3 border-t border-border pt-4">
                <Notice>Yeni bir link oluşturulur, geçerlilik bugünden 15 gün olur. Müşteriye daha önce gönderdiğiniz link çalışmaz.</Notice>
                <div className="grid grid-cols-2 gap-2">
                  <Button variant="outline" onClick={() => setConfirmRenew(false)} disabled={share.isPending}>
                    Vazgeç
                  </Button>
                  <Button onClick={() => share.mutate({ action: "renew", sendEmail: false })} disabled={share.isPending}>
                    Evet, yenile
                  </Button>
                </div>
              </div>
            ) : (
              <Button variant="ghost" className="self-start" onClick={() => setConfirmRenew(true)}>
                Linki yenile
              </Button>
            )
          ) : null}
        </div>
      ) : share.isError ? (
        <div className="flex flex-col gap-3">
          <Notice>{errorMessage(share.error)}</Notice>
          <Button onClick={() => share.mutate({ action: "share", sendEmail: false })}>Tekrar dene</Button>
        </div>
      ) : (
        <p role="status" className="text-sm text-neutral-600">
          Link hazırlanıyor…
        </p>
      )}
    </DialogShell>
  );
}

function RespondDialog({ quote, status, onClose }: { quote: Quote; status: "Accepted" | "Rejected"; onClose: () => void }) {
  const refresh = useQuotesRefresh();
  const [note, setNote] = useState("");
  const mark = useMutation({
    mutationFn: () =>
      unwrap(
        status === "Accepted"
          ? api.POST("/api/quotes/{id}/mark-accepted", { params: { path: { id: quote.id } }, body: { note: note.trim() || null } })
          : api.POST("/api/quotes/{id}/mark-rejected", { params: { path: { id: quote.id } }, body: { note: note.trim() || null } }),
      ),
    onSuccess: async () => {
      await refresh();
      toast.success(status === "Accepted" ? "Teklif onaylandı olarak işaretlendi." : "Teklif reddedildi olarak işaretlendi.");
      onClose();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  return (
    <DialogShell
      open
      onOpenChange={(v) => !v && onClose()}
      title={status === "Accepted" ? "Müşteri onayladı" : "Müşteri reddetti"}
      description="Örneğin telefonla verilen cevabı kaydedin."
    >
      <Field label="Not (isteğe bağlı)">
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={2} />
      </Field>
      <DialogFooter>
        <Button variant="outline" onClick={onClose}>
          Vazgeç
        </Button>
        <Button onClick={() => mark.mutate()} disabled={mark.isPending}>
          {status === "Accepted" ? <Check aria-hidden /> : <X aria-hidden />}
          Kaydet
        </Button>
      </DialogFooter>
    </DialogShell>
  );
}

/** Satışa dönüştür: kalemler ve tutar tekliften; ödeme seçilir (varsayılan tamamı veresiye → müşterinin carisine). */
function ConvertDialog({ quote, onClose }: { quote: Quote; onClose: () => void }) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const [payment, setPayment] = useState<PaymentState>(initialPayment("Credit"));
  const [submitted, setSubmitted] = useState(false);
  const drafts = paymentDrafts(payment, quote.total);
  const problem = paymentError(quote.total, drafts, true);
  const convert = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/quotes/{id}/convert", {
          params: { path: { id: quote.id } },
          body: { payments: drafts.map((p) => ({ method: p.method, amount: p.amount, cashAccountId: null })), note: null },
        }),
      ),
    onSuccess: async (r) => {
      await Promise.all(
        [quotesKey, salesKey, productsKey, stockKey, ["contacts"], ["cash-accounts"], ["ledger"], ["timeline"]].map((queryKey) =>
          queryClient.invalidateQueries({ queryKey }),
        ),
      );
      for (const w of r.warnings) toast.warning(w);
      toast.success(`${r.sale.no} kaydedildi.`);
      router.push(`/panel/satislar/${r.sale.id}`);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <DialogShell open onOpenChange={(v) => !v && onClose()} title="Satışa dönüştür" description={`${quote.no} · ${quote.contactName}`}>
      <div className="flex flex-col gap-4">
        <TotalsSummary totals={quote} />
        <PaymentForm total={quote.total} value={payment} onChange={setPayment} />
        {submitted && problem && (
          <p role="alert" className="text-sm text-danger-600">
            {problem}
          </p>
        )}
        <Button
          size="lg"
          disabled={convert.isPending}
          onClick={() => {
            setSubmitted(true);
            if (!problem) convert.mutate();
          }}
        >
          <Check aria-hidden />
          {convert.isPending ? "Kaydediliyor…" : `Satışı kaydet · ${formatMoney(quote.total)}`}
        </Button>
      </div>
    </DialogShell>
  );
}
