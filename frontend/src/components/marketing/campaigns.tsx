"use client";

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Megaphone } from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { smsCreditKey } from "@/components/marketing/sms-credit";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import {
  campaignStatusNames,
  recipientStatusNames,
  skipReasonNames,
  type Campaign,
  type CampaignStatus,
  type RecipientSkipReason,
} from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatDateTime } from "@/lib/format";
import { can } from "@/lib/permissions";

export const campaignsKey = ["campaigns"] as const;

const number = (n: number) => n.toLocaleString("tr-TR");

function StatusBadge({ status }: { status: CampaignStatus }) {
  const variant = status === "Completed" ? "default" : status === "Cancelled" ? "outline" : "secondary";
  return <Badge variant={variant}>{campaignStatusNames[status]}</Badge>;
}

/** "12 Ekim 14:30" — zamanlanmış kampanyada gönderim zamanı, diğerlerinde başlangıç. */
function when(c: Campaign) {
  return formatDateTime(c.startedAt ?? c.scheduledAt);
}

/** Kampanyalar sekmesi: kampanya listesi (masaüstü tablo, telefonda kart). */
export function CampaignList() {
  const sms = useSession().me.platform.smsEnabled;
  const list = useInfiniteQuery({
    queryKey: [...campaignsKey, "list"],
    initialPageParam: 1,
    queryFn: ({ pageParam }) => unwrap(api.GET("/api/campaigns", { params: { query: { page: pageParam, pageSize: 25 } } })),
    getNextPageParam: (last, pages) => (pages.length * 25 < last.total ? pages.length + 1 : undefined),
    // Gönderilen kampanyanın sayıları kendiliğinden güncellensin.
    refetchInterval: (q) =>
      q.state.data?.pages.some((p) => p.items.some((c) => c.status === "Sending" || c.status === "Scheduled")) ? 5000 : false,
  });
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="flex flex-col gap-4">
      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        <EmptyState icon={<Megaphone aria-hidden className="size-8" />} title="Henüz kampanya yok">
          <p className="text-sm text-neutral-600">
            {sms
              ? "Bir segment seçip izinli müşterilerinize SMS gönderin. İzni olmayan ve çıkış yapan müşteriler kendiliğinden elenir."
              : "E-posta ve WhatsApp kampanyaları yakında burada. Şimdilik müşteri listenizi Müşteriler'den süzüp dışa aktarabilirsiniz."}
          </p>
        </EmptyState>
      ) : (
        <>
          <table className="hidden w-full border-y border-border text-sm md:table">
            <thead>
              <tr className="border-b border-border text-left text-xs text-neutral-600">
                <th className="py-2 pr-3 font-semibold">Kampanya</th>
                <th className="py-2 pr-3 font-semibold">Durum</th>
                <th className="py-2 pr-3 font-semibold">Tarih</th>
                <th className="py-2 pr-3 text-right font-semibold">Gönderildi</th>
                <th className="py-2 pr-3 text-right font-semibold">Elendi</th>
                <th className="py-2 text-right font-semibold">Kredi</th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.id} className="border-b border-border last:border-0">
                  <td className="py-3 pr-3">
                    <Link href={`/panel/kampanyalar/${c.id}`} className="font-semibold text-black underline-offset-4 hover:underline">
                      {c.name}
                    </Link>
                    <span className="block text-xs text-neutral-600">{c.targetName}</span>
                  </td>
                  <td className="py-3 pr-3">
                    <StatusBadge status={c.status} />
                  </td>
                  <td className="py-3 pr-3 text-neutral-700">{when(c)}</td>
                  <td className="py-3 pr-3 text-right tabular-nums">
                    {number(c.sentCount)} / {number(c.recipientCount)}
                  </td>
                  <td className="py-3 pr-3 text-right tabular-nums">{number(c.skippedCount)}</td>
                  <td className="py-3 text-right tabular-nums">{number(c.creditUsed)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <ul className="flex flex-col gap-2 md:hidden" aria-label="Kampanyalar">
            {items.map((c) => (
              <li key={c.id}>
                <Link href={`/panel/kampanyalar/${c.id}`} className="flex flex-col gap-1 border border-border px-4 py-3 hover:border-black">
                  <span className="flex items-start justify-between gap-2">
                    <span className="font-semibold break-words text-black">{c.name}</span>
                    <StatusBadge status={c.status} />
                  </span>
                  <span className="text-xs text-neutral-600">
                    {c.targetName} · {when(c)}
                  </span>
                  <span className="text-sm text-neutral-700 tabular-nums">
                    {number(c.sentCount)} gönderildi · {number(c.skippedCount)} elendi · {number(c.creditUsed)} kredi
                  </span>
                </Link>
              </li>
            ))}
          </ul>
          {list.hasNextPage && (
            <Button variant="outline" onClick={() => list.fetchNextPage()} disabled={list.isFetchingNextPage}>
              Daha fazla
            </Button>
          )}
        </>
      )}
    </div>
  );
}

const recipientFilters = [
  { key: "", label: "Tümü" },
  { key: "sent", label: "Gönderildi" },
  { key: "failed", label: "Başarısız" },
  { key: "skipped", label: "Elendi" },
  { key: "pending", label: "Sırada" },
] as const;

/** /panel/kampanyalar/[id]: özet sayılar, ilerleme, mesaj, alıcılar (süzgeçli), iptal. */
export function CampaignDetail() {
  const { id } = useParams<{ id: string }>();
  const { role } = useSession();
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<string>("");
  const campaign = useQuery({
    queryKey: [...campaignsKey, id],
    queryFn: () => unwrap(api.GET("/api/campaigns/{id}", { params: { path: { id } } })),
    refetchInterval: (q) => (q.state.data?.status === "Sending" || q.state.data?.status === "Scheduled" ? 3000 : false),
  });
  const recipients = useInfiniteQuery({
    queryKey: [...campaignsKey, id, "recipients", filter, campaign.data?.sentCount, campaign.data?.status],
    initialPageParam: 1,
    enabled: !!campaign.data,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET("/api/campaigns/{id}/recipients", {
          params: { path: { id }, query: { status: filter || undefined, page: pageParam, pageSize: 50 } },
        }),
      ),
    getNextPageParam: (last, pages) => (pages.length * 50 < last.total ? pages.length + 1 : undefined),
  });
  const cancel = useMutation({
    mutationFn: () => unwrap(api.POST("/api/campaigns/{id}/cancel", { params: { path: { id } } })),
    onSuccess: async (next) => {
      queryClient.setQueryData([...campaignsKey, id], next);
      await queryClient.invalidateQueries({ queryKey: campaignsKey });
      await queryClient.invalidateQueries({ queryKey: smsCreditKey });
      toast.success("Kampanya iptal edildi; gönderilmemiş alıcılara gitmeyecek.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (campaign.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (campaign.isError) return <QueryError error={campaign.error} onRetry={campaign.refetch} />;
  const c = campaign.data;
  const processed = c.sentCount + c.failedCount + c.skippedCount;
  const progress = c.recipientCount > 0 ? Math.round((processed / c.recipientCount) * 100) : 0;
  const cancellable = (c.status === "Scheduled" || c.status === "Sending") && can(role, "manageMarketing");
  const items = recipients.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="flex flex-col gap-5">
      <Link
        href="/panel/kampanyalar"
        className="flex min-h-11 w-fit items-center gap-2 text-sm font-semibold text-neutral-700 hover:text-black"
      >
        <ArrowLeft aria-hidden className="size-4" />
        Kampanyalar
      </Link>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="text-2xl font-bold tracking-tight break-words text-black">{c.name}</h1>
          <p className="text-sm text-neutral-600">
            {c.targetName} · {c.status === "Scheduled" ? "Gönderim" : "Başladı"}: {when(c)}
            {c.createdByName && ` · ${c.createdByName}`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <StatusBadge status={c.status} />
          {cancellable && (
            <Button
              variant="outline"
              disabled={cancel.isPending}
              onClick={() => {
                if (window.confirm("Kampanya iptal edilsin mi? Henüz gönderilmemiş müşterilere SMS gitmez.")) cancel.mutate();
              }}
            >
              İptal et
            </Button>
          )}
        </div>
      </div>

      {c.requestedAt && (
        <p className="border-l-2 border-brand bg-neutral-50 px-3 py-2 text-sm text-black">
          Pazarlama SMS&apos;leri sadece 09:00-21:00 arası gönderilir; gönderim {formatDateTime(c.scheduledAt)}&apos;e kaydırıldı.
        </p>
      )}
      {c.ranOutOfCredit && (
        <p className="border-l-2 border-brand bg-neutral-50 px-3 py-2 text-sm text-black">
          Gönderim sırasında SMS krediniz bitti; kalan müşteriler &quot;kredi yetersiz&quot; olarak elendi.
        </p>
      )}

      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          ["Alıcı", c.recipientCount],
          ["Gönderildi", c.sentCount],
          ["Elendi", c.skippedCount],
          ["Harcanan kredi", c.creditUsed],
        ].map(([label, value]) => (
          <div key={label} className="flex flex-col border border-border px-3 py-2">
            <dt className="text-xs text-neutral-600">{label}</dt>
            <dd className="text-xl font-semibold text-black tabular-nums">{number(value as number)}</dd>
          </div>
        ))}
      </dl>
      {c.failedCount > 0 && <p className="text-sm text-neutral-700">{number(c.failedCount)} SMS gönderilemedi (kredisi iade edildi).</p>}
      {c.status === "Sending" && (
        <div className="flex flex-col gap-1">
          <div
            className="h-2 w-full bg-neutral-100"
            role="progressbar"
            aria-label="Gönderim ilerlemesi"
            aria-valuenow={progress}
            aria-valuemin={0}
            aria-valuemax={100}
          >
            <div className="h-2 bg-brand" style={{ width: `${progress}%` }} />
          </div>
          <span className="text-xs text-neutral-600">%{progress}</span>
        </div>
      )}

      <section className="flex flex-col gap-2">
        <h2 className="text-base font-semibold text-black">Mesaj</h2>
        <p className="border border-border px-3 py-2 text-sm whitespace-pre-wrap break-words text-black">{c.body}</p>
        <p className="text-xs text-neutral-600">Her mesajın sonuna çıkış linki ve RET eklenir.</p>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-base font-semibold text-black">Alıcılar</h2>
        <ScrollRow role="group" aria-label="Alıcı süzgeci">
          {recipientFilters.map((f) => (
            <Chip key={f.key} active={filter === f.key} onClick={() => setFilter(f.key)}>
              {f.label}
            </Chip>
          ))}
        </ScrollRow>
        {c.status === "Scheduled" ? (
          <p className="text-sm text-neutral-600">Alıcılar gönderim başladığında o anki izinlere göre belirlenir.</p>
        ) : recipients.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : recipients.isError ? (
          <QueryError error={recipients.error} onRetry={recipients.refetch} />
        ) : items.length === 0 ? (
          <p className="text-sm text-neutral-600">Bu süzgeçte alıcı yok.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border border-y border-border" aria-label="Alıcı listesi">
            {items.map((r) => (
              <li key={r.id} className="flex items-start justify-between gap-3 py-3">
                <div className="flex min-w-0 flex-col">
                  <Link href={`/panel/musteriler/${r.contactId}`} className="truncate text-sm font-semibold text-black underline-offset-4 hover:underline">
                    {r.contactName}
                  </Link>
                  {r.phone && <span className="text-xs text-neutral-600 tabular-nums">{displayPhone(r.phone)}</span>}
                </div>
                <span className="shrink-0 text-right text-sm text-black">
                  {recipientStatusNames[r.status]}
                  {r.skipReason && (
                    <span className="block text-xs text-neutral-600">{skipReasonNames[r.skipReason as RecipientSkipReason] ?? r.skipReason}</span>
                  )}
                  {r.status === "Sent" && r.segments > 1 && <span className="block text-xs text-neutral-600">{r.segments} SMS</span>}
                </span>
              </li>
            ))}
          </ul>
        )}
        {recipients.hasNextPage && (
          <Button variant="outline" onClick={() => recipients.fetchNextPage()} disabled={recipients.isFetchingNextPage}>
            Daha fazla
          </Button>
        )}
      </section>
    </div>
  );
}
