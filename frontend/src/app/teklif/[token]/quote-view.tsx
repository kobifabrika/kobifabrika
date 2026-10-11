"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, FileDown, X } from "lucide-react";
import { useParams } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";

import { PublicMessage, PublicShell } from "@/components/public/public-shell";
import { TotalsSummary } from "@/components/sales/totals";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { API_URL, ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import { formatDay, formatMoney } from "@/lib/format";
import { formatQuantity } from "@/lib/sales";

function Done({ children }: { children: ReactNode }) {
  return (
    <section className="flex items-start gap-3 border-l-2 border-brand bg-neutral-50 px-4 py-4">
      <Check aria-hidden className="mt-0.5 size-5 shrink-0 text-black" />
      <p className="text-base text-black">{children}</p>
    </section>
  );
}

/** /teklif/[token]: işletmenin müşterisine gönderdiği teklif. Onayla / Reddet (isteğe bağlı not), PDF indir. */
export function QuoteView() {
  const { token } = useParams<{ token: string }>();
  const queryClient = useQueryClient();
  const key = ["public", "quote", token];
  const [rejecting, setRejecting] = useState(false);
  const [note, setNote] = useState("");
  const quote = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/public/quotes/{token}", { params: { path: { token } } })),
    retry: false,
  });
  const respond = useMutation({
    mutationFn: (action: "accept" | "reject") =>
      unwrap(
        action === "accept"
          ? api.POST("/api/public/quotes/{token}/accept", { params: { path: { token } }, body: { note: note.trim() || null } })
          : api.POST("/api/public/quotes/{token}/reject", { params: { path: { token } }, body: { note: note.trim() || null } }),
      ),
    onSuccess: (data) => queryClient.setQueryData(key, data),
  });

  useEffect(() => {
    if (quote.data) document.title = `${quote.data.businessName} · Teklif ${quote.data.no}`;
  }, [quote.data]);

  if (quote.isPending)
    return (
      <PublicShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </PublicShell>
    );

  if (quote.isError) {
    const gone = quote.error instanceof ApiError && quote.error.status === 410;
    return (
      <PublicShell>
        <PublicMessage title={gone ? "Bu teklifin süresi doldu" : "Teklif bulunamadı"}>
          {gone ? "Güncel teklif için işletmeyle iletişime geçin." : "Bağlantıyı kontrol edin veya işletmeden yenisini isteyin."}
        </PublicMessage>
      </PublicShell>
    );
  }

  const q = quote.data;
  const accepted = q.status === "Accepted" || q.status === "Converted";

  return (
    <PublicShell businessName={q.businessName} logoUrl={q.logoUrl} phone={q.businessPhone}>
      <section className="flex flex-col gap-1">
        <p className="text-base text-neutral-700">Sayın {q.contactName},</p>
        <h1 className="mt-2 text-xl font-bold text-black">
          Teklif <span className="tabular-nums">{q.no}</span>
        </h1>
        <p className="text-sm text-neutral-700">
          {formatDay(q.date)} · {formatDay(q.validUntil)} tarihine kadar geçerli
        </p>
        <span className="mt-2 text-4xl font-bold tracking-tight text-black tabular-nums">{formatMoney(q.total)}</span>
        <span className="text-xs text-neutral-600">KDV dahil</span>
      </section>

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold text-black">Kalemler</h2>
        <ul className="flex flex-col divide-y divide-border border-y border-black">
          {q.lines.map((l) => (
            <li key={l.id} className="flex items-start justify-between gap-3 py-3">
              <span className="flex min-w-0 flex-col">
                <span className="text-base font-semibold text-black">{l.description}</span>
                <span className="text-sm text-neutral-700 tabular-nums">
                  {formatQuantity(l.quantity, l.unit)} × {formatMoney(l.unitPrice)}
                  {l.lineDiscount > 0 && ` · indirim ${formatMoney(l.lineDiscount)}`}
                </span>
              </span>
              <span className="shrink-0 text-base font-semibold text-black tabular-nums">
                {formatMoney(l.lineTotal + l.allocatedDiscount)}
              </span>
            </li>
          ))}
        </ul>
        <TotalsSummary totals={q} />
        {q.notes && <p className="pt-2 text-base whitespace-pre-line text-black">{q.notes}</p>}
      </section>

      {accepted ? (
        <Done>Teklifi onayladınız, {q.businessName} bilgilendirildi. Teşekkürler.</Done>
      ) : q.status === "Rejected" ? (
        <section className="border-l-2 border-neutral-400 bg-neutral-50 px-4 py-4 text-base text-black">
          Teklifi reddettiniz; {q.businessName} bilgilendirildi.
        </section>
      ) : q.canRespond ? (
        <section className="flex flex-col gap-3">
          {rejecting ? (
            <>
              <label className="flex flex-col gap-2 text-sm font-semibold text-black">
                Nedenini yazmak ister misiniz? (isteğe bağlı)
                <Textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={3} />
              </label>
              <div className="grid grid-cols-2 gap-2">
                <Button variant="outline" onClick={() => setRejecting(false)} disabled={respond.isPending}>
                  Vazgeç
                </Button>
                <Button variant="destructive" onClick={() => respond.mutate("reject")} disabled={respond.isPending}>
                  <X aria-hidden />
                  Reddi gönder
                </Button>
              </div>
            </>
          ) : (
            <>
              <Button size="lg" onClick={() => respond.mutate("accept")} disabled={respond.isPending}>
                <Check aria-hidden />
                {respond.isPending ? "Gönderiliyor…" : "Teklifi onayla"}
              </Button>
              <Button size="lg" variant="outline" onClick={() => setRejecting(true)}>
                Reddet
              </Button>
            </>
          )}
          {respond.isError && <p className="text-sm text-danger-600">{errorMessage(respond.error)}</p>}
        </section>
      ) : null}

      <Button asChild variant="outline" className="self-start">
        <a href={`${API_URL}/api/public/quotes/${token}/pdf`} download>
          <FileDown aria-hidden />
          PDF indir
        </a>
      </Button>
    </PublicShell>
  );
}
