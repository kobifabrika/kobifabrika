"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { useMemo, useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Notice, selectClass } from "@/components/contacts/contact-bits";
import { Field, FormMessage } from "@/components/forms/field";
import { ContactPicker } from "@/components/sales/contact-picker";
import { useServices } from "@/components/staff/staff-services";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { useDebounced } from "@/components/catalog/product-list";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { Appointment, AppointmentSaved, Staff } from "@/lib/api/types";
import { hhmm, parseHhmm } from "@/lib/calendar";
import type { CartContact } from "@/lib/cart";
import { istanbulParts, istanbulToUtcIso } from "@/lib/format";

import { ResponsivePanel, useAppointmentRefresh } from "./appointment-bits";

export type AppointmentDraft = { staffId?: string; date: string; time?: string; contact?: CartContact | null; contactId?: string | null };

/** "Ayşe Yılmaz son 5 randevusundan 2'sine gelmedi." */
export function noShowText(name: string | undefined, w: { noShows: number; of: number }) {
  const suffix: Record<number, string> = { 1: "ine", 2: "sine", 3: "üne", 4: "üne", 5: "ine" };
  return `${name ?? "Müşteri"} son ${w.of} randevusundan ${w.noShows}'${suffix[w.noShows] ?? "ine"} gelmedi.`;
}

/**
 * Yeni randevu veya taşıma/düzenleme. Bitiş hizmet süresinden gelir ve değiştirilebilir. Kaydetmeden önce sunucudan uyarılar
 * alınır (çakışma, çalışma saati dışı, gelmeyen müşteri); çakışmayı sunucu ayrıca engeller.
 */
export function AppointmentForm({
  open,
  onOpenChange,
  draft,
  appointment,
  staff,
  onSaved,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  draft?: AppointmentDraft;
  appointment?: Appointment;
  staff: Staff[];
  onSaved?: (saved: AppointmentSaved) => void;
}) {
  const editing = !!appointment;
  const refresh = useAppointmentRefresh();
  const services = useServices();
  const start = appointment ? istanbulParts(appointment.startAt) : null;
  const end = appointment ? istanbulParts(appointment.endAt) : null;
  const [picked, setPicked] = useState<{ contact: CartContact | null } | null>(
    appointment ? { contact: { id: appointment.contactId, name: appointment.contactName, phone: appointment.contactPhone } } : draft?.contact ? { contact: draft.contact } : null,
  );
  const [staffId, setStaffId] = useState(appointment?.staffMemberId ?? draft?.staffId ?? staff[0]?.id ?? "");
  const [serviceId, setServiceId] = useState(appointment?.productId ?? "");
  const [date, setDate] = useState(start?.date ?? draft?.date ?? "");
  const [time, setTime] = useState(start?.time ?? draft?.time ?? "10:00");
  const [endTime, setEndTime] = useState(end?.time ?? "");
  const [endTouched, setEndTouched] = useState(false);
  const [note, setNote] = useState(appointment?.note ?? "");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  // Müşteri kartından "Randevu ver": kullanıcı seçim yapana kadar müşteri önceden seçili gelir.
  const prefill = useQuery({
    queryKey: ["contacts", "detail", draft?.contactId],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}", { params: { path: { id: draft!.contactId! } } })),
    enabled: !!draft?.contactId && !appointment,
  });
  const contact: CartContact | null = picked
    ? picked.contact
    : prefill.data
      ? { id: prefill.data.id, name: prefill.data.displayName, phone: prefill.data.phone }
      : null;
  const setContact = (c: CartContact | null) => setPicked({ contact: c });

  const member = staff.find((s) => s.id === staffId);
  const offered = useMemo(
    () => (services.data ?? []).filter((s) => !member || member.allServices || member.serviceIds.includes(s.id) || s.id === appointment?.productId),
    [services.data, member, appointment?.productId],
  );

  // Bitiş elle değiştirilmediyse: hizmet seçilince hizmet süresinden, başlangıç değişince süre korunarak hesaplanır (taşıma).
  const setEndFrom = (nextTime: string, duration: number | null | undefined) => {
    if (!duration || duration <= 0 || !nextTime) return;
    setEndTime(hhmm(Math.min(parseHhmm(nextTime) + duration, 23 * 60 + 59)));
  };
  const serviceDuration = (id: string) => (services.data ?? []).find((s) => s.id === id)?.durationMinutes;
  const currentDuration = () => (time && endTime ? parseHhmm(endTime) - parseHhmm(time) : serviceDuration(serviceId));

  const startAt = date && time ? istanbulToUtcIso(date, time) : null;
  const endAt = date && endTime ? istanbulToUtcIso(date, endTime) : null;
  const checkInput = useDebounced(
    startAt && endAt && staffId && endAt > startAt ? { contactId: contact?.id ?? null, staffMemberId: staffId, startAt, endAt, excludeId: appointment?.id ?? null } : null,
    300,
  );
  const check = useQuery({
    queryKey: ["appointments", "check", checkInput],
    queryFn: () => unwrap(api.POST("/api/appointments/check", { body: checkInput! })),
    enabled: open && !!checkInput,
  });

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        appointment
          ? api.PUT("/api/appointments/{id}", {
              params: { path: { id: appointment.id } },
              body: { staffMemberId: staffId, productId: serviceId, startAt: startAt!, endAt: endAt!, note: note.trim() || null },
            })
          : api.POST("/api/appointments", {
              body: { contactId: contact!.id, staffMemberId: staffId, productId: serviceId, startAt: startAt!, endAt: endAt, note: note.trim() || null },
            }),
      ),
    onSuccess: async (saved) => {
      await refresh();
      toast.success(appointment ? "Randevu güncellendi." : "Randevu verildi.");
      for (const w of saved.warnings) toast.warning(w);
      onSaved?.(saved);
      onOpenChange(false);
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(fields);
      setError(Object.keys(fields).length ? null : errorMessage(e));
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (!contact) next.contactId = "Müşteri seçin.";
    if (!serviceId) next.productId = "Hizmet seçin.";
    if (!staffId) next.staffMemberId = "Personel seçin.";
    if (!date) next.startAt = "Tarih seçin.";
    if (!time) next.startAt = "Saat girin.";
    if (!endTime) next.endAt = "Bitiş saatini girin.";
    else if (time && parseHhmm(endTime) <= parseHhmm(time)) next.endAt = "Bitiş başlangıçtan sonra olmalı.";
    setErrors(next);
    setError(null);
    if (Object.keys(next).length === 0) save.mutate();
  };

  const warnings = check.data;
  return (
    <ResponsivePanel open={open} onOpenChange={onOpenChange} title={editing ? "Randevuyu taşı / düzenle" : "Yeni randevu"} wide>
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        {editing ? (
          <p className="text-sm text-black">
            <span className="font-semibold">{appointment.contactName}</span>
          </p>
        ) : (
          <ContactPicker value={contact} onChange={setContact} required error={errors.contactId} />
        )}
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Personel" error={errors.staffMemberId}>
            <select className={selectClass} value={staffId} onChange={(e) => setStaffId(e.target.value)}>
              {staff.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Hizmet" error={errors.productId}>
            <select
              className={selectClass}
              value={serviceId}
              onChange={(e) => {
                setServiceId(e.target.value);
                setEndTouched(false);
                setEndFrom(time, serviceDuration(e.target.value));
              }}
            >
              <option value="">Seçin</option>
              {offered.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                  {s.durationMinutes ? ` (${s.durationMinutes} dk)` : ""}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
          <Field label="Tarih" error={errors.startAt} className="col-span-2 sm:col-span-1">
            <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label="Başlangıç">
            <Input
              type="time"
              step={300}
              value={time}
              onChange={(e) => {
                if (!endTouched) setEndFrom(e.target.value, currentDuration());
                setTime(e.target.value);
              }}
            />
          </Field>
          <Field label="Bitiş" error={errors.endAt}>
            <Input type="time" step={300} value={endTime} onChange={(e) => (setEndTime(e.target.value), setEndTouched(true))} />
          </Field>
        </div>
        <Field label="Not (isteğe bağlı)">
          <Textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={2} />
        </Field>

        {warnings?.noShow && <Notice>{noShowText(contact?.name, warnings.noShow)}</Notice>}
        {warnings?.outsideHours && <Notice>{warnings.outsideHours} Yine de kaydedebilirsiniz.</Notice>}
        {warnings?.overlap && <FormMessage>{warnings.overlap}</FormMessage>}
        {error && <FormMessage>{error}</FormMessage>}

        <div className="flex gap-2">
          <Button type="submit" disabled={save.isPending}>
            {editing ? "Kaydet" : "Randevu ver"}
          </Button>
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            Vazgeç
          </Button>
        </div>
      </form>
    </ResponsivePanel>
  );
}
