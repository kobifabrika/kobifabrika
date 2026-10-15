"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, ExternalLink } from "lucide-react";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";

import { PublicMessage, PublicShell } from "@/components/public/public-shell";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { API_URL, ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import type { PublicSurvey } from "@/lib/api/types";
import { formatDay, istanbulParts } from "@/lib/format";

/**
 * Müşterinin anket sayfası: 5 büyük puan düğmesi (rakam). Puan verilince: 1-3'te önce "Bize iletin" yorum kutusu, Google linki
 * altta sade; 4-5'te Google düğmesi önde. Google linki her puanda görünür (review gating yasak). Puan tek kez verilir.
 */
export function SurveyView() {
  const { kod } = useParams<{ kod: string }>();
  const queryClient = useQueryClient();
  const key = ["public", "survey", kod];
  const [comment, setComment] = useState("");
  const survey = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/public/surveys/{code}", { params: { path: { code: kod } } })),
    retry: false,
  });
  const score = useMutation({
    mutationFn: (value: number) => unwrap(api.POST("/api/public/surveys/{code}/score", { params: { path: { code: kod } }, body: { score: value } })),
    onSuccess: (data) => queryClient.setQueryData(key, data),
    onError: () => survey.refetch(),
  });
  const send = useMutation({
    mutationFn: () => unwrap(api.POST("/api/public/surveys/{code}/comment", { params: { path: { code: kod } }, body: { comment: comment.trim() } })),
    onSuccess: (data) => queryClient.setQueryData(key, data),
  });

  useEffect(() => {
    if (survey.data) document.title = `${survey.data.businessName} · Ziyaretinizi puanlayın`;
  }, [survey.data]);

  if (survey.isPending)
    return (
      <PublicShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </PublicShell>
    );
  if (survey.isError) {
    const gone = survey.error instanceof ApiError && survey.error.status === 410;
    return (
      <PublicShell>
        <PublicMessage title={gone ? "Bu anketin süresi doldu" : "Anket bulunamadı"}>
          {gone ? "Görüşleriniz için teşekkür ederiz." : "Linki kontrol edin."}
        </PublicMessage>
      </PublicShell>
    );
  }

  const s = survey.data;
  const when = istanbulParts(s.appointmentAt);
  return (
    <PublicShell businessName={s.businessName} logoUrl={s.logoUrl}>
      {s.score == null ? (
        <section className="flex flex-col gap-4">
          <div className="flex flex-col gap-1">
            <h1 className="text-xl font-bold text-black">Ziyaretinizi nasıl buldunuz?</h1>
            <p className="text-base text-neutral-700">
              {formatDay(when.date)} {when.time}
              {s.serviceName && ` · ${s.serviceName}`}
            </p>
          </div>
          <div role="group" aria-label="Puan (1 kötü, 5 çok iyi)" className="grid grid-cols-5 gap-2 sm:max-w-md">
            {[1, 2, 3, 4, 5].map((n) => (
              <button
                key={n}
                type="button"
                disabled={score.isPending}
                onClick={() => score.mutate(n)}
                aria-label={`${n} puan`}
                className="flex h-14 items-center justify-center rounded-lg border border-black text-xl font-bold text-black hover:bg-black hover:text-white disabled:opacity-50"
              >
                {n}
              </button>
            ))}
          </div>
          <div className="flex justify-between text-xs text-neutral-600 sm:max-w-md">
            <span>1 · Kötü</span>
            <span>5 · Çok iyi</span>
          </div>
          {score.isError && (
            <p role="alert" className="text-sm text-danger-600">
              {errorMessage(score.error)}
            </p>
          )}
        </section>
      ) : (
        <Result survey={s} comment={comment} setComment={setComment} send={send} code={kod} />
      )}
    </PublicShell>
  );
}

function GoogleLink({ url, code, prominent }: { url: string; code: string; prominent: boolean }) {
  const track = () => {
    // Sayfadan çıkarken de kaydolsun (keepalive).
    void fetch(`${API_URL}/api/public/surveys/${code}/google-click`, {
      method: "POST",
      keepalive: true,
    }).catch(() => undefined);
  };
  return prominent ? (
    <Button asChild className="w-full sm:w-auto">
      <a href={url} target="_blank" rel="noopener noreferrer" onClick={track}>
        <ExternalLink aria-hidden /> Google&apos;da yorum yaz
      </a>
    </Button>
  ) : (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      onClick={track}
      className="inline-flex min-h-11 w-fit items-center gap-2 text-sm text-neutral-700 underline decoration-neutral-300 underline-offset-2"
    >
      Google&apos;da yorum yaz
    </a>
  );
}

function Result({
  survey: s,
  comment,
  setComment,
  send,
  code,
}: {
  survey: PublicSurvey;
  comment: string;
  setComment: (v: string) => void;
  send: { mutate: () => void; isPending: boolean; isError: boolean; error: unknown };
  code: string;
}) {
  const feedbackFirst = s.emphasis === "FeedbackFirst";
  return (
    <section className="flex max-w-md flex-col gap-5">
      <div className="flex items-start gap-3 border-l-2 border-brand bg-neutral-50 px-4 py-4" role="status">
        <Check aria-hidden className="mt-0.5 size-5 shrink-0 text-black" />
        <p className="text-base text-black">
          Puanınız: <strong>{s.score}/5</strong>. Teşekkür ederiz!
        </p>
      </div>

      {feedbackFirst && s.canComment && (
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            send.mutate();
          }}
        >
          <label className="flex flex-col gap-2 text-base font-semibold text-black">
            Bize iletin: neyi daha iyi yapabiliriz?
            <Textarea value={comment} maxLength={1000} rows={4} onChange={(e) => setComment(e.target.value)} />
          </label>
          <p className="text-xs text-neutral-600">Yorumunuz sadece {s.businessName}&apos;e iletilir.</p>
          <Button type="submit" className="w-full sm:w-auto" disabled={send.isPending || !comment.trim()}>
            Gönder
          </Button>
          {send.isError && (
            <p role="alert" className="text-sm text-danger-600">
              {errorMessage(send.error)}
            </p>
          )}
        </form>
      )}
      {s.commented && <p className="text-base text-black">Yorumunuz {s.businessName}&apos;e iletildi.</p>}

      {s.googleReviewUrl && (
        <div className="flex flex-col gap-2">
          {!feedbackFirst && <p className="text-base text-black">Memnun kaldıysanız Google&apos;da birkaç kelime yazmanız bize çok yardımcı olur.</p>}
          <GoogleLink url={s.googleReviewUrl} code={code} prominent={!feedbackFirst} />
        </div>
      )}
    </section>
  );
}
