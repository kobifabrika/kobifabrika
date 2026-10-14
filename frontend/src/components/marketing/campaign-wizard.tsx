"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ArrowRight, Send } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useRef, useState } from "react";
import { toast } from "sonner";

import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { campaignsKey } from "@/components/marketing/campaigns";
import { smsCreditKey, TopUpButton } from "@/components/marketing/sms-credit";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, fieldErrors, problemCode, unwrap } from "@/lib/api/client";
import type { CampaignPreview, CampaignTarget } from "@/lib/api/types";
import { formatDateTime, istanbulToUtcIso, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { BUSINESS_VARIABLE, composeCounter, FIRST_NAME_VARIABLE, unknownVariables } from "@/lib/sms";

type Step = "hedef" | "mesaj" | "onizleme";
const steps: { key: Step; label: string }[] = [
  { key: "hedef", label: "Kime" },
  { key: "mesaj", label: "Mesaj" },
  { key: "onizleme", label: "Önizle ve gönder" },
];

const ALL = "__all";
const number = (n: number) => n.toLocaleString("tr-TR");

/** Seçim anahtarı → API hedefi: "__all" tüm müşteriler, hazır segment kodu veya kayıtlı segment Id'si. */
function toTarget(key: string, systemCodes: string[]): CampaignTarget {
  if (key === ALL) return { filter: { type: "customer" } } as CampaignTarget;
  if (systemCodes.includes(key)) return { segmentCode: key } as CampaignTarget;
  return { segmentId: key } as CampaignTarget;
}

/**
 * /panel/kampanyalar/yeni?adim=hedef|mesaj|onizleme&segment=: segment seç → mesaj yaz ({ad}, {isletme}; karakter ve SMS sayacı)
 * → önizle (gerçek alıcılardan 3 örnek, alıcı ve elenen sayıları, tahmini kredi) → hemen gönder veya zamanla.
 * Geri tuşu adımlar arasında çalışır; sayfa yenilenirse baştan.
 */
export function CampaignWizard() {
  const router = useRouter();
  const params = useSearchParams();
  const { role } = useSession();
  const queryClient = useQueryClient();
  const step = (steps.find((s) => s.key === params.get("adim"))?.key ?? "hedef") as Step;
  const [targetKey, setTargetKey] = useState(params.get("segment") ?? ALL);
  const [name, setName] = useState(`Kampanya ${new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long" }).format(new Date())}`);
  const [body, setBody] = useState("Merhaba {ad}, ");
  const [schedule, setSchedule] = useState<"now" | "later">("now");
  const [date, setDate] = useState(todayIstanbul());
  const [time, setTime] = useState("10:00");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const bodyRef = useRef<HTMLTextAreaElement>(null);

  const segments = useQuery({ queryKey: ["segments"], queryFn: () => unwrap(api.GET("/api/segments")) });
  const info = useQuery({ queryKey: [...campaignsKey, "compose-info"], queryFn: () => unwrap(api.GET("/api/campaigns/compose-info")) });
  const systemCodes = (segments.data ?? []).filter((s) => s.code).map((s) => s.code!);
  const target = toTarget(targetKey, systemCodes);
  const scheduledAt = schedule === "later" ? istanbulToUtcIso(date, time) : undefined;

  const go = (next: Step) => router.push(`/panel/kampanyalar/yeni?adim=${next}`, { scroll: true });

  // Hedef adımında canlı alıcı sayısı; önizlemede gerçek mesajla.
  const preview = useQuery({
    queryKey: [...campaignsKey, "preview", targetKey, step === "onizleme" ? body : "", step === "onizleme" ? scheduledAt : ""],
    enabled: segments.isSuccess && step !== "mesaj",
    queryFn: () =>
      unwrap(
        api.POST("/api/campaigns/preview", {
          body: { target, body: step === "onizleme" && body.trim() ? body : "x", scheduledAt: step === "onizleme" ? scheduledAt : undefined },
        }),
      ),
  });

  const send = useMutation({
    mutationFn: () => unwrap(api.POST("/api/campaigns", { body: { name, target, body, scheduledAt, kind: "Manual" } })),
    onSuccess: async (c) => {
      await queryClient.invalidateQueries({ queryKey: campaignsKey });
      await queryClient.invalidateQueries({ queryKey: smsCreditKey });
      toast.success(c.status === "Scheduled" && c.requestedAt ? `Kampanya ${formatDateTime(c.scheduledAt)} için zamanlandı.` : "Kampanya gönderime alındı.");
      router.push(`/panel/kampanyalar/${c.id}`);
    },
    onError: (e) => {
      setErrors(fieldErrors(e));
      toast.error(problemCode(e) === "insufficient_credit" ? errorMessage(e) : errorMessage(e));
    },
  });

  if (!can(role, "manageMarketing"))
    return <p className="text-sm text-neutral-600">Kampanyayı sadece Sahip ve Yönetici gönderebilir.</p>;
  if (segments.isError) return <QueryError error={segments.error} onRetry={segments.refetch} />;

  const unknown = unknownVariables(body);
  const counter = info.data ? composeCounter(body, info.data.businessName, info.data.footerSample) : null;
  const insert = (variable: string) => {
    const el = bodyRef.current;
    const start = el?.selectionStart ?? body.length;
    const end = el?.selectionEnd ?? body.length;
    const next = body.slice(0, start) + variable + body.slice(end);
    setBody(next);
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(start + variable.length, start + variable.length);
    });
  };
  const canContinueMessage = body.trim().length > 0 && unknown.length === 0 && name.trim().length > 0 && body.length <= (info.data?.maxBodyLength ?? 600);

  return (
    <div className="flex flex-col gap-5 lg:max-w-3xl">
      <Link
        href="/panel/kampanyalar"
        className="flex min-h-11 w-fit items-center gap-2 text-sm font-semibold text-neutral-700 hover:text-black"
      >
        <ArrowLeft aria-hidden className="size-4" />
        Kampanyalar
      </Link>
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-bold tracking-tight text-black">Yeni kampanya</h1>
        <ol className="flex gap-4 text-sm" aria-label="Adımlar">
          {steps.map((s, i) => (
            <li key={s.key} aria-current={s.key === step ? "step" : undefined} className={s.key === step ? "font-semibold text-black" : "text-neutral-600"}>
              <span className={s.key === step ? "border-b-2 border-brand pb-0.5" : ""}>
                {i + 1}. {s.label}
              </span>
            </li>
          ))}
        </ol>
      </div>

      {step === "hedef" && (
        <section className="flex flex-col gap-4" aria-label="Kime">
          <p className="text-sm text-neutral-700">Kampanya sadece SMS izni olan, çıkış yapmamış ve cep telefonu kayıtlı müşterilere gider.</p>
          <ScrollRow role="group" aria-label="Segment">
            <Chip active={targetKey === ALL} onClick={() => setTargetKey(ALL)}>
              Tüm müşteriler
            </Chip>
            {(segments.data ?? []).map((s) => {
              const key = s.code ?? s.id!;
              return (
                <Chip key={key} active={targetKey === key} onClick={() => setTargetKey(key)}>
                  {s.name}
                </Chip>
              );
            })}
          </ScrollRow>
          <TargetSummary preview={preview.data} loading={preview.isFetching} error={preview.error} onRetry={preview.refetch} />
          <div className="flex justify-end">
            <Button onClick={() => go("mesaj")} disabled={!preview.data || preview.data.recipients === 0} className="max-sm:w-full">
              Devam <ArrowRight aria-hidden />
            </Button>
          </div>
        </section>
      )}

      {step === "mesaj" && (
        <section className="flex flex-col gap-4" aria-label="Mesaj">
          <Field label="Kampanya adı" hint="Sadece sizin listenizde görünür." error={errors.name}>
            <Input value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field
            label="Mesaj"
            error={unknown.length > 0 ? `Bilinmeyen değişken: ${unknown.join(", ")}. Sadece {ad} ve {isletme} kullanılabilir.` : errors.body}
            hint={
              counter && (
                <span aria-live="polite" className="tabular-nums">
                  {number(counter.length)} karakter · {counter.segments} SMS (kişi başı){counter.unicode && " · Türkçe karakter var (70 karakterde 1 SMS)"}
                </span>
              )
            }
          >
            <Textarea ref={bodyRef} value={body} rows={5} maxLength={info.data?.maxBodyLength ?? 600} onChange={(e) => setBody(e.target.value)} />
          </Field>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={() => insert(FIRST_NAME_VARIABLE)}>
              + Ad ({"{ad}"})
            </Button>
            <Button type="button" variant="outline" onClick={() => insert(BUSINESS_VARIABLE)}>
              + İşletme ({"{isletme}"})
            </Button>
          </div>
          {info.data && (
            <div className="flex flex-col gap-1">
              <span className="text-xs font-semibold text-neutral-600">Her mesajın sonuna eklenir (değiştirilemez):</span>
              <p className="border border-dashed border-neutral-300 px-3 py-2 text-sm break-all text-neutral-700">{info.data.footerSample.replace("XXXXXXXX", "…")}</p>
            </div>
          )}
          <div className="flex justify-between gap-2">
            <Button variant="outline" onClick={() => go("hedef")}>
              <ArrowLeft aria-hidden /> Geri
            </Button>
            <Button onClick={() => go("onizleme")} disabled={!canContinueMessage}>
              Önizle <ArrowRight aria-hidden />
            </Button>
          </div>
        </section>
      )}

      {step === "onizleme" && (
        <section className="flex flex-col gap-5" aria-label="Önizle ve gönder">
          {preview.isPending ? (
            <p role="status" className="text-sm text-neutral-600">
              Hesaplanıyor…
            </p>
          ) : preview.isError ? (
            <QueryError error={preview.error} onRetry={preview.refetch} />
          ) : (
            <PreviewBody preview={preview.data} />
          )}

          <fieldset className="flex flex-col gap-3">
            <legend className="mb-2 text-sm font-semibold text-black">Ne zaman?</legend>
            <ScrollRow role="group" aria-label="Gönderim zamanı">
              <Chip active={schedule === "now"} onClick={() => setSchedule("now")}>
                Hemen gönder
              </Chip>
              <Chip active={schedule === "later"} onClick={() => setSchedule("later")}>
                İleri tarihe zamanla
              </Chip>
            </ScrollRow>
            {schedule === "later" && (
              <div className="grid max-w-md grid-cols-2 gap-3">
                <Field label="Tarih">
                  <Input type="date" value={date} min={todayIstanbul()} onChange={(e) => setDate(e.target.value)} />
                </Field>
                <Field label="Saat">
                  <Input type="time" value={time} step={900} onChange={(e) => setTime(e.target.value)} />
                </Field>
              </div>
            )}
            {preview.data?.shifted && (
              <p className="border-l-2 border-brand bg-neutral-50 px-3 py-2 text-sm text-black">
                Pazarlama SMS&apos;leri sadece {preview.data.windowStart}-{preview.data.windowEnd} arası gönderilir. Gönderim{" "}
                <strong>{formatDateTime(preview.data.sendAt)}</strong>&apos;e kayacak.
              </p>
            )}
          </fieldset>
          {errors.scheduledAt && <FormMessage>{errors.scheduledAt}</FormMessage>}

          <div className="flex justify-between gap-2">
            <Button variant="outline" onClick={() => go("mesaj")}>
              <ArrowLeft aria-hidden /> Geri
            </Button>
            <Button
              onClick={() => send.mutate()}
              disabled={send.isPending || !preview.data || preview.data.recipients === 0 || !preview.data.enoughCredit || preview.isFetching}
            >
              <Send aria-hidden /> {schedule === "now" && !preview.data?.shifted ? "Gönder" : "Zamanla"}
            </Button>
          </div>
        </section>
      )}
    </div>
  );
}

function TargetSummary({
  preview,
  loading,
  error,
  onRetry,
}: {
  preview?: CampaignPreview;
  loading: boolean;
  error: unknown;
  onRetry: () => unknown;
}) {
  if (error) return <QueryError error={error} onRetry={onRetry} />;
  if (!preview)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Hesaplanıyor…
      </p>
    );
  const excluded = preview.noConsent + preview.optedOut + preview.noPhone + preview.dailyLimit;
  return (
    <div className="flex flex-col gap-1 border border-border px-4 py-3" aria-live="polite" aria-busy={loading}>
      <span className="text-lg font-semibold text-black tabular-nums">{number(preview.recipients)} kişiye gidecek</span>
      <span className="text-sm text-neutral-700 tabular-nums">
        Segmentte {number(preview.total)} müşteri{excluded > 0 && ` · ${number(excluded)} elendi`}
      </span>
    </div>
  );
}

function PreviewBody({ preview: p }: { preview: CampaignPreview }) {
  const rows: [string, number][] = [
    ["Gönderilecek", p.recipients],
    ["İzni yok", p.noConsent],
    ["Çıkış yapmış", p.optedOut],
    ["Cep telefonu yok", p.noPhone],
    ["Bugün SMS almış", p.dailyLimit],
  ];
  return (
    <div className="flex flex-col gap-5">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        {rows
          .filter(([label, value]) => label === "Gönderilecek" || value > 0)
          .map(([label, value]) => (
            <div key={label} className="flex flex-col border border-border px-3 py-2">
              <dt className="text-xs text-neutral-600">{label}</dt>
              <dd className="text-xl font-semibold text-black tabular-nums">{number(value)}</dd>
            </div>
          ))}
        <div className="flex flex-col border border-black px-3 py-2">
          <dt className="text-xs text-neutral-600">Tahmini kredi</dt>
          <dd className="text-xl font-semibold text-black tabular-nums">
            {number(p.estimatedCredits)} <span className="text-sm font-normal text-neutral-700">/ bakiye {number(p.balance)}</span>
          </dd>
        </div>
      </dl>
      {!p.enoughCredit && (
        <div className="flex flex-wrap items-center justify-between gap-3 border-l-2 border-brand bg-neutral-50 px-3 py-2">
          <p className="text-sm text-black">
            Krediniz yetmiyor: {number(p.estimatedCredits)} kredi gerekiyor, bakiyeniz {number(p.balance)}. Bakiye {number(p.affordableRecipients)} kişiye
            yeter.
          </p>
          <TopUpButton />
        </div>
      )}
      <section className="flex flex-col gap-2" aria-label="Örnek mesajlar">
        <h2 className="text-base font-semibold text-black">Örnek mesajlar</h2>
        {p.samples.length === 0 ? (
          <p className="text-sm text-neutral-600">Gönderilecek müşteri yok.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {p.samples.map((s) => (
              <li key={s.contactName + s.text} className="flex flex-col gap-1 border border-border px-3 py-2">
                <span className="text-xs font-semibold text-neutral-600">
                  {s.contactName} · {s.segments} SMS
                </span>
                <span className="text-sm break-words text-black">{s.text}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
