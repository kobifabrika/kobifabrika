"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { toast } from "sonner";

import { selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { NumberInput } from "@/components/ui/number-input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import {
  automationDescriptions,
  automationNames,
  automationSlugs,
  type Automation,
  type AutomationType,
} from "@/lib/api/types";
import { can } from "@/lib/permissions";

export const automationsKey = ["automations"] as const;

const number = (n: number) => n.toLocaleString("tr-TR");

function useAutomations() {
  return useQuery({ queryKey: [...automationsKey], queryFn: () => unwrap(api.GET("/api/automations")) });
}

function saveBody(a: Automation, patch: Partial<Automation> = {}) {
  const next = { ...a, ...patch };
  return {
    isEnabled: next.isEnabled,
    body: next.body,
    sendAt: next.sendAt ?? null,
    inactiveDays: next.inactiveDays ?? null,
    repeatDays: next.repeatDays ?? null,
    delayMinutes: next.delayMinutes ?? null,
  };
}

/** Kampanyalar → Otomasyonlar: her biri aç/kapa, kısa açıklama, son 30 gün SMS ve kredi, Düzenle. */
export function AutomationList() {
  const { role, me } = useSession();
  const sms = me.platform.smsEnabled;
  const queryClient = useQueryClient();
  const list = useAutomations();
  const toggle = useMutation({
    mutationFn: ({ a, on }: { a: Automation; on: boolean }) =>
      unwrap(api.PUT("/api/automations/{type}", { params: { path: { type: a.type } }, body: saveBody(a, { isEnabled: on }) })),
    onSuccess: async (a) => {
      await queryClient.invalidateQueries({ queryKey: automationsKey });
      toast.success(`${automationNames[a.type]} ${a.isEnabled ? "açıldı" : "kapatıldı"}.`);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const manage = can(role, "manageMarketing");

  if (list.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (list.isError) return <QueryError error={list.error} onRetry={list.refetch} />;

  return (
    <div className="flex flex-col gap-3">
      {sms ? (
        <p className="text-sm text-neutral-700">
          Otomasyonlar varsayılan kapalıdır. Pazarlama mesajları sadece SMS izni olan müşterilere, 09:00-21:00 arası ve aynı kişiye günde en fazla
          bir kez gider.
        </p>
      ) : (
        <p className="border-l-2 border-brand bg-neutral-50 px-4 py-3 text-sm text-neutral-700">
          Otomasyonlar SMS ile çalışır; SMS pilot döneminde kapalı olduğu için şimdilik açılamaz. Mesaj metinlerini hazırlayabilirsiniz. E-posta
          ile otomasyonlar yakında.
        </p>
      )}
      <ul className="flex flex-col divide-y divide-border border-y border-border" aria-label="Otomasyonlar">
        {list.data.map((a) => (
          <li key={a.type} className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex min-w-0 flex-col gap-1">
              <span className="text-base font-semibold text-black">{automationNames[a.type]}</span>
              <span className="text-sm text-neutral-700">{automationDescriptions[a.type]}</span>
              {sms && (
                <span className="text-xs text-neutral-600 tabular-nums">
                  Son 30 gün: {number(a.sentLast30Days)} SMS · {number(a.creditsLast30Days)} kredi
                  {!a.isMarketing && " · bilgilendirme (izin aranmaz)"}
                </span>
              )}
            </div>
            <div className="flex shrink-0 items-center gap-3">
              <label className="flex min-h-11 items-center gap-2 text-sm text-black">
                <Switch
                  checked={a.isEnabled}
                  disabled={!manage || !sms || toggle.isPending}
                  onCheckedChange={(on) => toggle.mutate({ a, on })}
                  aria-label={`${automationNames[a.type]} açık`}
                />
                {a.isEnabled ? "Açık" : "Kapalı"}
              </label>
              <Button asChild variant="outline">
                <Link href={`/panel/kampanyalar/otomasyonlar/${automationSlugs[a.type]}`} aria-label={`${automationNames[a.type]}: düzenle`}>
                  {manage ? "Düzenle" : "Görüntüle"}
                </Link>
              </Button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

const delayOptions = [
  [0, "Hemen"],
  [30, "30 dakika sonra"],
  [60, "1 saat sonra"],
  [120, "2 saat sonra"],
  [240, "4 saat sonra"],
  [1440, "Ertesi gün aynı saatte"],
] as const;

const times = Array.from({ length: 24 }, (_, i) => 9 * 60 + i * 30)
  .filter((m) => m < 21 * 60)
  .map((m) => `${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")}`);

/** /panel/kampanyalar/otomasyonlar/[tur]: aç/kapa, mesaj şablonu (değişkenler), saat veya gecikme, X/Y günü, önizleme. */
export function AutomationEditor() {
  const { tur } = useParams<{ tur: string }>();
  const type = (Object.entries(automationSlugs).find(([, slug]) => slug === tur)?.[0] ?? null) as AutomationType | null;
  const list = useAutomations();
  if (!type) return <p className="text-sm text-neutral-600">Otomasyon bulunamadı.</p>;
  if (list.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (list.isError) return <QueryError error={list.error} onRetry={list.refetch} />;
  const a = list.data.find((x) => x.type === type)!;
  return <EditorForm key={a.type} automation={a} />;
}

function EditorForm({ automation }: { automation: Automation }) {
  const { role, me } = useSession();
  const router = useRouter();
  const queryClient = useQueryClient();
  const readOnly = !can(role, "manageMarketing");
  const [form, setForm] = useState(automation);
  const bodyRef = useRef<HTMLTextAreaElement>(null);
  const set = (patch: Partial<Automation>) => setForm((f) => ({ ...f, ...patch }));

  const preview = useMutation({
    mutationFn: () => unwrap(api.POST("/api/automations/{type}/preview", { params: { path: { type: form.type } }, body: saveBody(form) })),
    onError: (e) => toast.error(errorMessage(e)),
  });
  const save = useMutation({
    mutationFn: () => unwrap(api.PUT("/api/automations/{type}", { params: { path: { type: form.type } }, body: saveBody(form) })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: automationsKey });
      toast.success("Otomasyon kaydedildi.");
      router.push("/panel/kampanyalar?sekme=otomasyonlar");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const insert = (variable: string) => {
    const el = bodyRef.current;
    const start = el?.selectionStart ?? form.body.length;
    const end = el?.selectionEnd ?? form.body.length;
    set({ body: form.body.slice(0, start) + variable + form.body.slice(end) });
    requestAnimationFrame(() => el?.focus());
  };
  const variableNames: Record<string, string> = { "{ad}": "Ad", "{isletme}": "İşletme", "{link}": "Link", "{tutar}": "Tutar" };
  const daily = form.type === "Birthday" || form.type === "WinBack" || form.type === "DueReminder";

  return (
    <div className="flex flex-col gap-5 lg:max-w-2xl">
      <Link
        href="/panel/kampanyalar?sekme=otomasyonlar"
        className="flex min-h-11 w-fit items-center gap-2 text-sm font-semibold text-neutral-700 hover:text-black"
      >
        <ArrowLeft aria-hidden className="size-4" />
        Otomasyonlar
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-bold tracking-tight text-black">{automationNames[form.type]}</h1>
        <p className="text-sm text-neutral-700">{automationDescriptions[form.type]}</p>
      </div>

      <label className="flex min-h-11 items-center gap-3 text-base font-semibold text-black">
        <Switch
          checked={form.isEnabled}
          disabled={readOnly || !me.platform.smsEnabled}
          onCheckedChange={(on) => set({ isEnabled: on })}
          aria-label="Otomasyon açık"
        />
        {form.isEnabled ? "Açık" : "Kapalı"}
      </label>

      <Field label="Mesaj" hint={form.isMarketing ? "Sonuna çıkış linki ve RET eklenir." : "Bilgilendirme mesajı: izin aranmaz, çıkış eki yok."}>
        <Textarea ref={bodyRef} rows={4} maxLength={400} value={form.body} disabled={readOnly} onChange={(e) => set({ body: e.target.value })} />
      </Field>
      {!readOnly && (
        <div className="flex flex-wrap gap-2">
          {form.variables.map((v) => (
            <Button key={v} type="button" variant="outline" onClick={() => insert(v)}>
              + {variableNames[v] ?? v} ({v})
            </Button>
          ))}
        </div>
      )}

      {daily && (
        <Field label="Gönderim saati" hint="Her gün bu saatte bir kez (İstanbul).">
          <select className={selectClass} value={form.sendAt ?? "10:00"} disabled={readOnly} onChange={(e) => set({ sendAt: e.target.value })}>
            {times.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </Field>
      )}
      {form.type === "WinBack" && (
        <div className="grid grid-cols-2 gap-3">
          <Field label="Kaç gündür gelmeyen" hint="Son ziyaretten bu kadar gün sonra.">
            <NumberInput kind="integer" value={form.inactiveDays ?? ""} disabled={readOnly}
              onChange={(e) => set({ inactiveDays: Number(e.target.value.replace(/\D/g, "")) || null })} />
          </Field>
          <Field label="En fazla kaç günde bir" hint="Aynı müşteriye tekrar için.">
            <NumberInput kind="integer" value={form.repeatDays ?? ""} disabled={readOnly}
              onChange={(e) => set({ repeatDays: Number(e.target.value.replace(/\D/g, "")) || null })} />
          </Field>
        </div>
      )}
      {(form.type === "VisitThanks" || form.type === "Survey") && (
        <Field label="Ne zaman" hint="Randevu tamamlandı olarak işaretlendikten sonra. Teşekkür ve anket ikisi açıksa tek SMS'te birleşir.">
          <select className={selectClass} value={form.delayMinutes ?? 120} disabled={readOnly}
            onChange={(e) => set({ delayMinutes: Number(e.target.value) })}>
            {delayOptions.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </Field>
      )}

      {preview.data && (
        <section className="flex flex-col gap-1 border border-border px-3 py-2" aria-label="Önizleme">
          <span className="text-xs font-semibold text-neutral-600">
            Örnek · {preview.data.segments} SMS{preview.data.today != null && ` · bugün ${number(preview.data.today)} kişi`}
          </span>
          <span className="text-sm break-words text-black">{preview.data.text}</span>
        </section>
      )}

      {!readOnly && (
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="outline" onClick={() => preview.mutate()} disabled={preview.isPending}>
            Önizle
          </Button>
          <Button onClick={() => save.mutate()} disabled={save.isPending}>
            Kaydet
          </Button>
        </div>
      )}
    </div>
  );
}
