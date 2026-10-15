"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { selectClass } from "@/components/contacts/contact-bits";
import { Field, FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { staffColorNames, type Staff, type StaffColor, type StaffRequest } from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { normalizePhone, PHONE_MESSAGE } from "@/lib/validation/rules";

import { StaffSwatch, useStaffRefresh } from "./staff-bits";

const colors = Object.keys(staffColorNames) as StaffColor[];

/** Personel bilgileri (ekleme ve düzenleme). Eklenince personel kartının çalışma saatleri sekmesine geçer. */
export function StaffForm({ staff, readOnly = false }: { staff?: Staff; readOnly?: boolean }) {
  const router = useRouter();
  const refresh = useStaffRefresh();
  const [form, setForm] = useState<StaffRequest>({
    fullName: staff?.fullName ?? "",
    title: staff?.title ?? "",
    phone: staff?.phone ? displayPhone(staff.phone) : "",
    color: staff?.color ?? "Black",
    showInBooking: staff?.showInBooking ?? true,
    userId: staff?.userId ?? null,
    isActive: staff?.isActive ?? true,
    startDate: staff?.startDate ?? null,
    birthDate: staff?.birthDate ?? null,
    notes: staff?.notes ?? "",
  });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const members = useQuery({
    queryKey: ["team", "members"],
    queryFn: () => unwrap(api.GET("/api/team/members")),
    enabled: !readOnly,
  });

  const save = useMutation({
    mutationFn: (body: StaffRequest) =>
      unwrap(
        staff
          ? api.PUT("/api/staff/{id}", { params: { path: { id: staff.id } }, body })
          : api.POST("/api/staff", { body }),
      ),
    onSuccess: async (saved) => {
      await refresh();
      toast.success(staff ? "Personel kaydedildi." : "Personel eklendi. Çalışma saatlerini kontrol edin.");
      if (!staff) router.push(`/panel/personel/${saved.id}?sekme=saatler`);
    },
    onError: (e) => {
      setErrors(fieldErrors(e));
      setError(Object.keys(fieldErrors(e)).length ? null : errorMessage(e));
    },
  });

  const set = <K extends keyof StaffRequest>(key: K, value: StaffRequest[K]) => setForm((f) => ({ ...f, [key]: value }));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (form.fullName.trim().length < 2) next.fullName = "Ad soyad girin.";
    const phone = form.phone?.trim() ? normalizePhone(form.phone) : null;
    if (form.phone?.trim() && !phone) next.phone = PHONE_MESSAGE;
    setErrors(next);
    if (Object.keys(next).length) return;
    save.mutate({
      ...form,
      fullName: form.fullName.trim(),
      title: form.title?.trim() || null,
      phone,
      startDate: form.startDate || null,
      birthDate: form.birthDate || null,
      notes: form.notes?.trim() || null,
    });
  };

  return (
    <form onSubmit={submit} noValidate className="flex max-w-xl flex-col gap-5">
      <Field label="Ad soyad" error={errors.fullName}>
        <Input value={form.fullName} onChange={(e) => set("fullName", e.target.value)} disabled={readOnly} maxLength={100} autoComplete="off" />
      </Field>
      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Unvan (isteğe bağlı)" error={errors.title} hint="Ör. Kuaför, Estetisyen, Usta">
          <Input value={form.title ?? ""} onChange={(e) => set("title", e.target.value)} disabled={readOnly} maxLength={100} />
        </Field>
        <Field label="Telefon (isteğe bağlı)" error={errors.phone}>
          <Input value={form.phone ?? ""} onChange={(e) => set("phone", e.target.value)} disabled={readOnly} inputMode="tel" autoComplete="off" />
        </Field>
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="İşe başlama (isteğe bağlı)" error={errors.startDate} hint="Yıllık izin hakkı kıdemden hesaplanır.">
          <Input type="date" value={form.startDate ?? ""} onChange={(e) => set("startDate", e.target.value || null)} disabled={readOnly} />
        </Field>
        <Field label="Doğum tarihi (isteğe bağlı)" error={errors.birthDate} hint="Sadece izin hakkı hesabı için (18 yaş ve altı, 50 yaş ve üstü).">
          <Input type="date" value={form.birthDate ?? ""} onChange={(e) => set("birthDate", e.target.value || null)} disabled={readOnly} />
        </Field>
      </div>
      <Field label="Not (isteğe bağlı)" error={errors.notes} hint="Sağlık bilgisi ve kimlik numarası yazmayın.">
        <Textarea value={form.notes ?? ""} onChange={(e) => set("notes", e.target.value)} disabled={readOnly} maxLength={1000} rows={3} />
      </Field>

      <fieldset className="flex flex-col gap-2">
        <legend className="mb-2 text-sm font-semibold text-black">Takvim rengi</legend>
        <div role="radiogroup" aria-label="Takvim rengi" className="flex flex-wrap gap-2">
          {colors.map((c) => (
            <button
              key={c}
              type="button"
              role="radio"
              aria-checked={form.color === c}
              disabled={readOnly}
              onClick={() => set("color", c)}
              className={`flex min-h-11 items-center gap-2 rounded-lg border px-3 text-sm font-semibold ${
                form.color === c ? "border-black ring-2 ring-black" : "border-border hover:border-black"
              }`}
            >
              <StaffSwatch color={c} className="size-4" />
              {staffColorNames[c]}
            </button>
          ))}
        </div>
      </fieldset>

      <Field label="Bağlı kullanıcı hesabı (isteğe bağlı)" hint="Bağlanan kullanıcı kendi telefonundan mesaiye başlar, izin talep eder; Personel rolündeyse Bugün ekranında kendi randevularını görür.">
        <select
          className={selectClass}
          value={form.userId ?? ""}
          disabled={readOnly}
          onChange={(e) => set("userId", e.target.value || null)}
        >
          <option value="">Bağlı hesap yok</option>
          {readOnly && staff?.userId && <option value={staff.userId}>{staff.userName}</option>}
          {members.data?.map((m) => (
            <option key={m.userId} value={m.userId}>
              {m.fullName} ({m.email})
            </option>
          ))}
        </select>
      </Field>

      <label className="flex min-h-14 items-center justify-between gap-4 border-t border-border pt-4">
        <span className="flex flex-col">
          <span className="text-sm font-semibold text-black">Online randevuda görünsün</span>
          <span className="text-sm text-neutral-600">Müşteriler online randevu sayfasında bu personeli seçebilir.</span>
        </span>
        <Switch checked={form.showInBooking} disabled={readOnly} onCheckedChange={(v) => set("showInBooking", v)} />
      </label>
      {staff && (
        <label className="flex min-h-14 items-center justify-between gap-4 border-t border-border pt-4">
          <span className="flex flex-col">
            <span className="text-sm font-semibold text-black">Aktif</span>
            <span className="text-sm text-neutral-600">Pasif personel takvimde görünmez, randevu verilemez. Geçmiş kayıtları korunur.</span>
          </span>
          <Switch checked={form.isActive} disabled={readOnly} onCheckedChange={(v) => set("isActive", v)} />
        </label>
      )}

      {error && <FormMessage>{error}</FormMessage>}
      {!readOnly && (
        <Button type="submit" className="self-start" disabled={save.isPending}>
          {staff ? "Kaydet" : "Personeli ekle"}
        </Button>
      )}
    </form>
  );
}
