"use client";

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { QueryError } from "@/components/feedback/query-error";
import { Field } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { SurveySummary } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { can } from "@/lib/permissions";

export const surveysKey = ["surveys"] as const;

/** Kampanyalar → Anketler: son 90 gün özeti (ortalama, dağılım, Google tıklaması), Google yorum linki, cevaplar. */
export function SurveyResults() {
  const [score, setScore] = useState<number | null>(null);
  const summary = useQuery({ queryKey: [...surveysKey, "summary"], queryFn: () => unwrap(api.GET("/api/surveys/summary")) });
  const list = useInfiniteQuery({
    queryKey: [...surveysKey, "list", score],
    initialPageParam: 1,
    queryFn: ({ pageParam }) => unwrap(api.GET("/api/surveys", { params: { query: { score: score ?? undefined, page: pageParam, pageSize: 25 } } })),
    getNextPageParam: (last, pages) => (pages.length * 25 < last.total ? pages.length + 1 : undefined),
  });
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="flex flex-col gap-5">
      {summary.isError ? (
        <QueryError error={summary.error} onRetry={summary.refetch} />
      ) : summary.data ? (
        <>
          <SummaryCard summary={summary.data} />
          <ReviewLink summary={summary.data} />
        </>
      ) : (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      )}

      <section className="flex flex-col gap-3" aria-label="Anket cevapları">
        <h2 className="text-base font-semibold text-black">Cevaplar</h2>
        <ScrollRow role="group" aria-label="Puan süzgeci">
          <Chip active={score === null} onClick={() => setScore(null)}>
            Tümü
          </Chip>
          {[1, 2, 3, 4, 5].map((n) => (
            <Chip key={n} active={score === n} onClick={() => setScore(n)}>
              {n} puan
            </Chip>
          ))}
        </ScrollRow>
        {list.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : list.isError ? (
          <QueryError error={list.error} onRetry={list.refetch} />
        ) : items.length === 0 ? (
          <p className="text-sm text-neutral-600">
            Henüz anket yok. Otomasyonlar sekmesinden &quot;Memnuniyet anketi&quot;ni açın; randevu tamamlanınca müşteriye link gider.
          </p>
        ) : (
          <ul className="flex flex-col divide-y divide-border border-y border-border" aria-label="Anket listesi">
            {items.map((s) => (
              <li key={s.id} className="flex flex-col gap-1 py-3">
                <div className="flex items-start justify-between gap-3">
                  <Link href={`/panel/musteriler/${s.contactId}`} className="truncate text-sm font-semibold text-black underline-offset-4 hover:underline">
                    {s.contactName}
                  </Link>
                  <span className="shrink-0 text-sm font-semibold text-black tabular-nums">{s.score != null ? `${s.score}/5` : "Cevap yok"}</span>
                </div>
                <span className="text-xs text-neutral-600">
                  {formatDateTime(s.answeredAt ?? s.sentAt)}
                  {s.staffName && ` · ${s.staffName}`}
                  {s.googleClicked && " · Google'a gitti"}
                </span>
                {s.comment && <p className="border-l-2 border-brand bg-neutral-50 px-3 py-2 text-sm break-words text-black">{s.comment}</p>}
              </li>
            ))}
          </ul>
        )}
        {list.hasNextPage && (
          <Button variant="outline" onClick={() => list.fetchNextPage()} disabled={list.isFetchingNextPage}>
            Daha fazla
          </Button>
        )}
      </section>
    </div>
  );
}

function SummaryCard({ summary: s }: { summary: SurveySummary }) {
  const max = Math.max(1, ...s.distribution);
  return (
    <section className="grid gap-4 border border-border px-4 py-4 sm:grid-cols-2" aria-label="Anket özeti (son 90 gün)">
      <dl className="grid grid-cols-2 gap-3">
        <div className="flex flex-col">
          <dt className="text-xs text-neutral-600">Ortalama</dt>
          <dd className="text-3xl font-semibold text-black tabular-nums">{s.average != null ? s.average.toLocaleString("tr-TR") : "—"}</dd>
        </div>
        <div className="flex flex-col">
          <dt className="text-xs text-neutral-600">Cevaplanan</dt>
          <dd className="text-xl font-semibold text-black tabular-nums">
            {s.answered} / {s.sent}
          </dd>
        </div>
        <div className="col-span-2 flex flex-col">
          <dt className="text-xs text-neutral-600">Google&apos;da yorum yazmaya giden</dt>
          <dd className="text-base font-semibold text-black tabular-nums">{s.googleClicks}</dd>
        </div>
      </dl>
      <ul className="flex flex-col gap-1.5" aria-label="Puan dağılımı">
        {[5, 4, 3, 2, 1].map((n) => {
          const count = s.distribution[n - 1] ?? 0;
          return (
            <li key={n} className="flex items-center gap-2 text-sm text-black">
              <span className="w-12 shrink-0 tabular-nums">{n} puan</span>
              <span className="h-3 flex-1 bg-neutral-100" aria-hidden>
                <span className={`block h-3 ${n >= 4 ? "bg-brand" : "bg-black"}`} style={{ width: `${(count / max) * 100}%` }} />
              </span>
              <span className="w-8 shrink-0 text-right tabular-nums">{count}</span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function ReviewLink({ summary }: { summary: SurveySummary }) {
  const { role } = useSession();
  const queryClient = useQueryClient();
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | undefined>();
  const save = useMutation({
    mutationFn: () => unwrap(api.PUT("/api/business/review-settings", { body: { googleReviewUrl: (url ?? "").trim() || null } })),
    onSuccess: (next) => {
      queryClient.setQueryData([...surveysKey, "summary"], next);
      setUrl(null);
      setError(undefined);
      toast.success("Google yorum linki kaydedildi.");
    },
    onError: (e) => setError(fieldErrors(e).googleReviewUrl ?? errorMessage(e)),
  });
  const readOnly = !can(role, "manageBusiness");
  const value = url ?? summary.googleReviewUrl ?? "";

  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      <Field
        label="Google yorum linki"
        error={error}
        hint="Ankette herkese gösterilir (Google, sadece memnun müşterilere link göstermeyi yasaklar). 1-3 puanda önce “Bize iletin” kutusu, 4-5'te Google düğmesi öne çıkar."
      >
        <Input type="url" inputMode="url" placeholder="https://g.page/r/…/review" value={value} disabled={readOnly} onChange={(e) => setUrl(e.target.value)} />
      </Field>
      {!readOnly && (
        <Button type="submit" variant="outline" className="self-start" disabled={save.isPending || url === null}>
          Kaydet
        </Button>
      )}
    </form>
  );
}
