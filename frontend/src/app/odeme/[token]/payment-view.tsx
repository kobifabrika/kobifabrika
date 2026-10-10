"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Copy } from "lucide-react";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import { PublicMessage, PublicShell } from "@/components/public/public-shell";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import { formatMoney } from "@/lib/format";
import { formatIban } from "@/lib/validation/rules";

export function CopyRow({ label, value, display }: { label: string; value: string; display?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex items-center gap-3 border-b border-border py-3 last:border-0">
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="text-xs text-neutral-600">{label}</span>
        <span className="text-base font-semibold break-all text-black tabular-nums">{display ?? value}</span>
      </div>
      <Button
        type="button"
        variant="outline"
        aria-label={`${label}: kopyala`}
        onClick={async () => {
          await navigator.clipboard.writeText(value);
          setCopied(true);
          setTimeout(() => setCopied(false), 2000);
        }}
      >
        {copied ? <Check aria-hidden /> : <Copy aria-hidden />}
        {copied ? "Kopyalandı" : "Kopyala"}
      </Button>
    </div>
  );
}

function Done({ children }: { children: React.ReactNode }) {
  return (
    <section className="flex items-start gap-3 border-l-2 border-brand bg-neutral-50 px-4 py-4">
      <Check aria-hidden className="mt-0.5 size-5 shrink-0 text-black" />
      <p className="text-base text-black">{children}</p>
    </section>
  );
}

export function PaymentView() {
  const { token } = useParams<{ token: string }>();
  const queryClient = useQueryClient();
  const key = ["public", "payment", token];
  const [note, setNote] = useState("");
  const payment = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/public/payment-requests/{token}", { params: { path: { token } } })),
    retry: false,
  });
  const claim = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/public/payment-requests/{token}/claim", {
          params: { path: { token } },
          body: { note: note.trim() || null },
        }),
      ),
    onSuccess: (data) => queryClient.setQueryData(key, data),
  });

  useEffect(() => {
    if (payment.data) document.title = `${payment.data.businessName} · Ödeme bilgileri`;
  }, [payment.data]);

  if (payment.isPending) {
    return (
      <PublicShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </PublicShell>
    );
  }

  if (payment.isError) {
    const gone = payment.error instanceof ApiError && payment.error.status === 410;
    return (
      <PublicShell>
        <PublicMessage title={gone ? "Bu ödeme bağlantısı artık geçerli değil" : "Ödeme bağlantısı bulunamadı"}>
          {gone
            ? "Süresi dolmuş veya işletme tarafından kapatılmış. Güncel bilgi için işletmeyle iletişime geçin."
            : "Bağlantıyı kontrol edin veya işletmeden yenisini isteyin."}
        </PublicMessage>
      </PublicShell>
    );
  }

  const p = payment.data;

  return (
    <PublicShell businessName={p.businessName} logoUrl={p.logoUrl} phone={p.businessPhone}>
      <section className="flex flex-col gap-1">
        <p className="text-base text-neutral-700">Merhaba {p.contactName},</p>
        <h1 className="mt-2 text-sm font-semibold text-neutral-700">Ödenecek tutar</h1>
        <span className="text-4xl font-bold tracking-tight text-black tabular-nums">{formatMoney(p.amount)}</span>
        {p.note && <p className="mt-2 text-base text-black">{p.note}</p>}
      </section>

      {p.status === "Confirmed" ? (
        <Done>Ödemeniz alındı, teşekkürler. {p.businessName} ödemenizi onayladı.</Done>
      ) : (
        <>
          <section className="flex flex-col gap-2">
            <h2 className="text-lg font-semibold text-black">Havale / EFT bilgileri</h2>
            {p.iban ? (
              <div className="flex flex-col border-y border-black">
                <CopyRow label="IBAN" value={p.iban} display={formatIban(p.iban)} />
                <CopyRow label="Alıcı adı" value={p.ibanHolderName} />
                <CopyRow label="Açıklama (adınızı yazın)" value={p.contactName} />
              </div>
            ) : (
              <p className="text-sm text-neutral-700">
                İşletme IBAN bilgisini kaldırmış. Ödeme için işletmeyle iletişime geçin.
              </p>
            )}
          </section>

          {p.status === "ClaimedPaid" ? (
            <Done>Teşekkürler, bildiriminiz iletildi. {p.businessName} ödemeyi hesabında görünce onaylayacak.</Done>
          ) : (
            <section className="flex flex-col gap-3">
              <p className="text-sm text-neutral-700">
                Ödemeyi yaptıktan sonra aşağıdaki butona dokunun; işletmeye haber verilir.
              </p>
              <label className="flex flex-col gap-2 text-sm font-semibold text-black">
                Not (isteğe bağlı)
                <Textarea
                  rows={2}
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                  maxLength={500}
                  placeholder="ör. Garanti BBVA hesabımdan gönderdim"
                />
              </label>
              {claim.isError && (
                <p role="alert" className="text-sm text-danger-600">
                  {errorMessage(claim.error)}
                </p>
              )}
              <Button
                size="lg"
                className="w-full sm:w-auto sm:self-start"
                onClick={() => claim.mutate()}
                disabled={claim.isPending}
              >
                {claim.isPending ? "Gönderiliyor…" : "Ödedim"}
              </Button>
            </section>
          )}
        </>
      )}
    </PublicShell>
  );
}
