"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, ExternalLink } from "lucide-react";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Notice, selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { BookingSettings, BookingSettingsRequest } from "@/lib/api/types";
import { can } from "@/lib/permissions";

export const bookingSettingsKey = ["business", "booking-settings"] as const;

const minNoticeOptions: [number, string][] = [
  [0, "Hemen (bekleme yok)"],
  [30, "30 dakika sonrası"],
  [60, "1 saat sonrası"],
  [120, "2 saat sonrası"],
  [180, "3 saat sonrası"],
  [360, "6 saat sonrası"],
  [720, "12 saat sonrası"],
  [1440, "1 gün sonrası"],
  [2880, "2 gün sonrası"],
];
const reminderOptions: [number, string][] = [
  [0, "Gönderme"],
  [1, "1 saat önce"],
  [2, "2 saat önce"],
  [3, "3 saat önce"],
  [6, "6 saat önce"],
  [12, "12 saat önce"],
  [24, "24 saat önce"],
  [48, "48 saat önce"],
  [72, "72 saat önce"],
];
const cancelOptions: [number, string][] = [
  [0, "Müşteri online iptal edemez"],
  [1, "En az 1 saat kala"],
  [2, "En az 2 saat kala"],
  [3, "En az 3 saat kala"],
  [6, "En az 6 saat kala"],
  [12, "En az 12 saat kala"],
  [24, "En az 24 saat kala"],
  [48, "En az 48 saat kala"],
];
const slotOptions = [5, 10, 15, 20, 30, 60];

/** Seçenek listesinde olmayan kayıtlı değer de görünsün (ör. API'den 90 dk). */
const withCurrent = (options: [number, string][], value: number, label: (v: number) => string): [number, string][] =>
  options.some(([v]) => v === value) ? options : [...options, [value, label(value)] as [number, string]].sort((a, b) => a[0] - b[0]);

function bookingLink(slug: string) {
  return typeof window === "undefined" ? `/r/${slug}` : `${window.location.origin}/r/${slug}`;
}

/**
 * Ayarlar → Randevu → Online randevu: sayfa açık/kapalı (varsayılan kapalı; en az bir "online randevuda görünsün" personeli gerekir),
 * otomatik onay, en erken randevu, en fazla gün, saat aralığı, hatırlatma SMS'i ve müşterinin iptal süresi; link kopyala/önizle.
 */
export function BookingSettingsCard() {
  const settings = useQuery({ queryKey: bookingSettingsKey, queryFn: () => unwrap(api.GET("/api/business/booking-settings")) });
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">Online randevu</CardTitle>
        <CardDescription>Müşterileriniz randevu linkinizden hizmet, personel ve saat seçip SMS koduyla randevu alır.</CardDescription>
      </CardHeader>
      <CardContent>
        {settings.isPending ? (
          <p role="status" className="text-sm text-neutral-600">
            Yükleniyor…
          </p>
        ) : settings.isError ? (
          <QueryError error={settings.error} onRetry={settings.refetch} />
        ) : (
          <BookingSettingsForm key={JSON.stringify(settings.data)} initial={settings.data} />
        )}
      </CardContent>
    </Card>
  );
}

function BookingSettingsForm({ initial }: { initial: BookingSettings }) {
  const { role } = useSession();
  const readOnly = !can(role, "manageBusiness");
  const queryClient = useQueryClient();
  const [form, setForm] = useState<BookingSettingsRequest>({
    enabled: initial.enabled,
    autoConfirm: initial.autoConfirm,
    minNoticeMinutes: initial.minNoticeMinutes,
    maxDaysAhead: initial.maxDaysAhead,
    slotMinutes: initial.slotMinutes,
    reminderHoursBefore: initial.reminderHoursBefore,
    customerCancelHours: initial.customerCancelHours,
  });
  const [maxDaysText, setMaxDaysText] = useState(String(initial.maxDaysAhead));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: (body: BookingSettingsRequest) => unwrap(api.PUT("/api/business/booking-settings", { body })),
    onSuccess: (next) => {
      queryClient.setQueryData(bookingSettingsKey, next);
      toast.success(next.enabled ? "Online randevu ayarları kaydedildi. Sayfanız açık." : "Online randevu ayarları kaydedildi.");
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(fields);
      setError(Object.keys(fields).length ? null : errorMessage(e));
    },
  });
  const set = (patch: Partial<BookingSettingsRequest>) => setForm((f) => ({ ...f, ...patch }));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const maxDays = Number(maxDaysText);
    if (!Number.isInteger(maxDays) || maxDays < 1 || maxDays > 180) {
      setErrors({ maxDaysAhead: "En fazla gün 1 ile 180 arasında olmalı." });
      return;
    }
    setErrors({});
    setError(null);
    save.mutate({ ...form, maxDaysAhead: maxDays });
  };

  const link = bookingLink(initial.slug);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(link);
      toast.success("Link kopyalandı.");
    } catch {
      toast.error("Kopyalanamadı; linki seçip kopyalayın.");
    }
  };

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <label className="flex min-h-14 items-center justify-between gap-4">
        <span className="flex flex-col">
          <span className="text-sm font-semibold text-black">Online randevu sayfası açık</span>
          <span className="text-sm text-neutral-600">
            Açmak için en az bir personelde &quot;Online randevuda görünsün&quot; açık olmalı. Online sayfada {initial.bookableStaff} personel,{" "}
            {initial.bookableServices} hizmet görünür.
          </span>
        </span>
        <Switch aria-label="Online randevu sayfası açık" checked={form.enabled} disabled={readOnly} onCheckedChange={(v) => set({ enabled: v })} />
      </label>
      {initial.bookableServices === 0 && (
        <Notice>Hizmetin online sayfada çıkması için aktif olması ve süresinin tanımlı olması gerekir (Ürünler/Hizmetler).</Notice>
      )}

      <div className="flex flex-col gap-2">
        <span className="text-sm font-semibold text-black">Randevu linkiniz</span>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          <code className="min-h-11 flex-1 truncate rounded-lg border border-border bg-neutral-50 px-3 py-2.5 text-sm text-black">{link}</code>
          <div className="flex gap-2">
            <Button type="button" variant="outline" onClick={copy}>
              <Copy aria-hidden />
              Kopyala
            </Button>
            <Button asChild variant="outline">
              <a href={`/r/${initial.slug}`} target="_blank" rel="noopener">
                <ExternalLink aria-hidden />
                Önizle
              </a>
            </Button>
          </div>
        </div>
        {!initial.enabled && <p className="text-xs text-neutral-600">Sayfa kapalıyken link &quot;bulunamadı&quot; gösterir.</p>}
      </div>

      <label className="flex min-h-14 items-center justify-between gap-4">
        <span className="flex flex-col">
          <span className="text-sm font-semibold text-black">Online randevuları otomatik onayla</span>
          <span className="text-sm text-neutral-600">Kapalıyken online randevu &quot;Beklemede&quot; düşer; takvimden onaylarsınız.</span>
        </span>
        <Switch aria-label="Otomatik onay" checked={form.autoConfirm} disabled={readOnly} onCheckedChange={(v) => set({ autoConfirm: v })} />
      </label>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="En erken randevu" hint="Müşteri bu süreden sonrasına randevu alabilir.">
          <select
            className={selectClass}
            disabled={readOnly}
            value={form.minNoticeMinutes}
            onChange={(e) => set({ minNoticeMinutes: Number(e.target.value) })}
          >
            {withCurrent(minNoticeOptions, form.minNoticeMinutes, (v) => `${v} dakika sonrası`).map(([v, label]) => (
              <option key={v} value={v}>
                {label}
              </option>
            ))}
          </select>
        </Field>
        <Field label="En fazla kaç gün ilerisi" error={errors.maxDaysAhead} hint="1 ile 180 gün arası.">
          <Input type="number" inputMode="numeric" min={1} max={180} disabled={readOnly} value={maxDaysText} onChange={(e) => setMaxDaysText(e.target.value)} />
        </Field>
        <Field label="Saat aralığı" error={errors.slotMinutes} hint="Müşteriye gösterilen saatler bu aralıkla başlar (09:00, 09:15…).">
          <select className={selectClass} disabled={readOnly} value={form.slotMinutes} onChange={(e) => set({ slotMinutes: Number(e.target.value) })}>
            {slotOptions.map((v) => (
              <option key={v} value={v}>
                {v} dakika
              </option>
            ))}
          </select>
        </Field>
        <Field label="Hatırlatma SMS'i" hint="Onaylı randevudan önce müşteriye bir kez gider.">
          <select
            className={selectClass}
            disabled={readOnly}
            value={form.reminderHoursBefore}
            onChange={(e) => set({ reminderHoursBefore: Number(e.target.value) })}
          >
            {withCurrent(reminderOptions, form.reminderHoursBefore, (v) => `${v} saat önce`).map(([v, label]) => (
              <option key={v} value={v}>
                {label}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Müşterinin online iptali" hint="SMS'teki linkten ve onay sayfasından.">
          <select
            className={selectClass}
            disabled={readOnly}
            value={form.customerCancelHours}
            onChange={(e) => set({ customerCancelHours: Number(e.target.value) })}
          >
            {withCurrent(cancelOptions, form.customerCancelHours, (v) => `En az ${v} saat kala`).map(([v, label]) => (
              <option key={v} value={v}>
                {label}
              </option>
            ))}
          </select>
        </Field>
      </div>

      {error && <FormMessage>{error}</FormMessage>}
      {!readOnly && (
        <Button type="submit" className="self-start" disabled={save.isPending}>
          Kaydet
        </Button>
      )}
    </form>
  );
}
