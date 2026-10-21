"use client";

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Mail, MessageCircle } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { ResponsivePanel } from "@/components/appointments/appointment-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { NumberInput } from "@/components/ui/number-input";
import { supportEmail, supportWhatsappLink } from "@/content/support";
import { API_URL, api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { smsCreditKindNames, type SmsCreditSummary } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { can } from "@/lib/permissions";

export const smsCreditKey = ["sms-credits"] as const;

export function useSmsCredit(enabled = true) {
  return useQuery({ queryKey: [...smsCreditKey], queryFn: () => unwrap(api.GET("/api/sms-credits")), enabled });
}

const number = (n: number) => n.toLocaleString("tr-TR");

/** "Kredi yükle": ödeme Sprint 8'de; şimdilik destekten istenir (WhatsApp/e-posta; ikisi de yoksa metin). */
export function TopUpButton({ variant = "outline" }: { variant?: "outline" | "default" }) {
  const [open, setOpen] = useState(false);
  const { business } = useSession();
  const whatsapp = supportWhatsappLink(`Merhaba, ${business.name} işletmesi için SMS kredisi yüklemek istiyorum.`);
  return (
    <>
      <Button type="button" variant={variant} onClick={() => setOpen(true)}>
        Kredi yükle
      </Button>
      <ResponsivePanel open={open} onOpenChange={setOpen} title="Kredi yükle">
        <div className="flex flex-col gap-4">
          <p className="text-sm text-neutral-700">
            Kartla kredi yükleme yakında. Şimdilik kredi yüklemek için destekten isteyin; yükleme yapılınca bakiyeniz
            burada görünür.
          </p>
          {whatsapp || supportEmail ? (
            <div className="flex flex-col gap-2 sm:flex-row">
              {whatsapp && (
                <Button asChild>
                  <a href={whatsapp} target="_blank" rel="noopener noreferrer">
                    <MessageCircle aria-hidden /> WhatsApp ile destek
                  </a>
                </Button>
              )}
              {supportEmail && (
                <Button asChild variant="outline">
                  <a href={`mailto:${supportEmail}?subject=${encodeURIComponent("SMS kredisi")}`}>
                    <Mail aria-hidden /> {supportEmail}
                  </a>
                </Button>
              )}
            </div>
          ) : (
            <p className="border-l-2 border-brand bg-neutral-50 px-3 py-2 text-sm text-black">
              Kredi yüklemek için KobiFabrika destek ekibine ulaşın.
            </p>
          )}
        </div>
      </ResponsivePanel>
    </>
  );
}

/** Bakiye şeridi (kampanya ekranlarının üstünde). */
export function CreditStrip() {
  const { role } = useSession();
  const credit = useSmsCredit();
  if (!credit.data) return null;
  const c = credit.data;
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border border-border px-4 py-3">
      <div className="flex flex-col">
        <span className="text-xs text-neutral-600">SMS kredisi</span>
        <span className="text-lg font-semibold text-black tabular-nums">
          {number(c.balance)} kredi
          {c.isLow && <span className="ml-2 text-sm font-normal text-neutral-700">· azaldı</span>}
        </span>
      </div>
      <div className="flex items-center gap-2">
        {can(role, "viewSettings") && (
          <Button asChild variant="ghost">
            <Link href="/panel/ayarlar/sms">Hareketler</Link>
          </Button>
        )}
        <TopUpButton />
      </div>
    </div>
  );
}

/** Ayarlar → SMS: bakiye, uyarı eşiği, kredi hareketleri. */
export function SmsSettings() {
  const { role, me } = useSession();
  const credit = useSmsCredit(me.platform.smsEnabled);
  if (!me.platform.smsEnabled)
    return (
      <p className="border-l-2 border-brand bg-neutral-50 px-4 py-3 text-sm text-neutral-700">
        SMS gönderimi pilot döneminde kapalı. Randevu bildirimleri WhatsApp&apos;tan ve e-postayla yapılır.
      </p>
    );
  return (
    <div className="flex flex-col gap-4">
      {role === "Accountant" && (
        <p className="text-sm text-neutral-600">Mali Müşavir olarak bu sayfayı görüntüleyebilirsiniz; değiştiremezsiniz.</p>
      )}
      {credit.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : credit.isError ? (
        <QueryError error={credit.error} onRetry={credit.refetch} />
      ) : (
        <BalanceCard summary={credit.data} readOnly={!can(role, "manageBusiness")} />
      )}
      <Transactions />
    </div>
  );
}

function BalanceCard({ summary: c, readOnly }: { summary: SmsCreditSummary; readOnly: boolean }) {
  const queryClient = useQueryClient();
  const [threshold, setThreshold] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: (lowBalanceThreshold: number) =>
      unwrap(api.PUT("/api/business/sms-settings", { body: { lowBalanceThreshold } })),
    onSuccess: (next) => {
      queryClient.setQueryData([...smsCreditKey], next);
      setThreshold(null);
      setError(null);
      toast.success("Uyarı eşiği kaydedildi.");
    },
    onError: (e) => setError(fieldErrors(e).lowBalanceThreshold ?? errorMessage(e)),
  });
  const value = threshold ?? String(c.lowBalanceThreshold);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">SMS kredisi</CardTitle>
        <CardDescription>
          1 kredi = 1 SMS (Türkçe karakterli mesaj 70, diğerleri 160 karakterde bir kredi). Doğrulama kodları krediden
          düşmez.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="grid gap-4 sm:grid-cols-3">
          <div className="flex flex-col">
            <span className="text-sm text-neutral-600">Bakiye</span>
            <span className="text-3xl font-semibold text-black tabular-nums" data-testid="sms-balance">
              {number(c.balance)}
            </span>
            {c.isLow && (
              <span className="mt-1 border-l-2 border-brand pl-2 text-sm text-black">
                Bakiye uyarı eşiğinin altında.
              </span>
            )}
          </div>
          <div className="flex flex-col">
            <span className="text-sm text-neutral-600">Son 30 gün</span>
            <span className="text-base text-black tabular-nums">
              {number(c.sentLast30Days)} SMS · {number(c.spentLast30Days)} kredi
            </span>
          </div>
          <div className="flex items-start sm:justify-end">
            <TopUpButton variant="default" />
          </div>
        </div>
        <form
          className="flex flex-wrap items-end gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            const n = Number(value);
            if (!/^\d+$/.test(value) || n > 100000)
              return setError("Eşik 0 ile 100.000 arasında olmalı (0 = uyarı kapalı).");
            save.mutate(n);
          }}
        >
          <Field
            label="Düşük bakiye uyarısı"
            hint="Bakiye bunun altına inince Sahip ve Yöneticilere bildirim gider. 0 = kapalı."
            error={error ?? undefined}
            className="w-56"
          >
            <NumberInput
              kind="integer"
              value={value}
              disabled={readOnly}
              onChange={(e) => setThreshold(e.target.value)}
            />
          </Field>
          {!readOnly && (
            <Button type="submit" variant="outline" disabled={save.isPending || threshold === null}>
              Kaydet
            </Button>
          )}
        </form>
      </CardContent>
    </Card>
  );
}

function Transactions() {
  const { role } = useSession();
  const list = useInfiniteQuery({
    queryKey: [...smsCreditKey, "transactions"],
    initialPageParam: 1,
    queryFn: ({ pageParam }) =>
      unwrap(api.GET("/api/sms-credits/transactions", { params: { query: { page: pageParam, pageSize: 25 } } })),
    getNextPageParam: (last, pages) => (pages.length * 25 < last.total ? pages.length + 1 : undefined),
  });
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2">
        <CardTitle className="text-black">Kredi hareketleri</CardTitle>
        {can(role, "exportData") && (
          <Button asChild variant="outline">
            <a href={`${API_URL}/api/sms-credits/transactions.xlsx`} download>
              <Download aria-hidden />
              <span className="sr-only sm:not-sr-only">Excel&apos;e aktar</span>
            </a>
          </Button>
        )}
      </CardHeader>
      <CardContent>
        {list.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : list.isError ? (
          <QueryError error={list.error} onRetry={list.refetch} />
        ) : items.length === 0 ? (
          <p className="text-sm text-neutral-600">Henüz hareket yok.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border" aria-label="Kredi hareketleri">
            {items.map((t) => (
              <li key={t.id} className="flex items-start justify-between gap-3 py-3">
                <div className="flex min-w-0 flex-col">
                  <span className="text-sm font-semibold text-black">
                    {smsCreditKindNames[t.kind]}
                    {t.purpose && <span className="font-normal text-neutral-700"> · {t.purpose}</span>}
                  </span>
                  <span className="truncate text-xs text-neutral-600">
                    {formatDateTime(t.createdAt)}
                    {t.note && t.note !== smsCreditKindNames[t.kind] && ` · ${t.note}`}
                  </span>
                </div>
                <div className="flex shrink-0 flex-col items-end">
                  <span className="text-sm font-semibold text-black tabular-nums">
                    {t.amount > 0 ? `+${number(t.amount)}` : number(t.amount)}
                  </span>
                  <span className="text-xs text-neutral-600 tabular-nums">Bakiye {number(t.balanceAfter)}</span>
                </div>
              </li>
            ))}
          </ul>
        )}
        {list.hasNextPage && (
          <Button
            variant="outline"
            className="mt-3 w-full"
            onClick={() => list.fetchNextPage()}
            disabled={list.isFetchingNextPage}
          >
            Daha fazla
          </Button>
        )}
      </CardContent>
    </Card>
  );
}
