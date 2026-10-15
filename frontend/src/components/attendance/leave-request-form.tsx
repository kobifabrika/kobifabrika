"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { ChoiceGroup } from "@/components/catalog/catalog-bits";
import { selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { useMyStaff, useStaffList } from "@/components/staff/staff-bits";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { halfDayNames, type HalfDay } from "@/lib/api/types";
import { addDays } from "@/lib/date-ranges";
import { todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";

import { leaveKey, useAttendanceRefresh, useLeaveTypes } from "./attendance-bits";
import { PreviewSummary } from "./leave-requests";

/**
 * İzin talebi: tür, ilk/son gün, yarım gün (tek günde), not; canlı önizleme (sayılan gün, bakiye, çakışan randevular).
 * Personel kendisi için talep eder (Sahip/Yönetici onaylar); Sahip/Yönetici personel seçer ve girdiği izin onaylı başlar.
 */
export function LeaveRequestForm() {
  const { role } = useSession();
  const manager = can(role, "manageStaffTime");
  const router = useRouter();
  const refresh = useAttendanceRefresh();
  const types = useLeaveTypes();
  const me = useMyStaff();
  const staff = useStaffList();
  const tomorrow = addDays(todayIstanbul(), 1);
  const [staffId, setStaffId] = useState<string | null>(null);
  const [typeId, setTypeId] = useState<string | null>(null);
  const [start, setStart] = useState(tomorrow);
  const [end, setEnd] = useState(tomorrow);
  const [halfDay, setHalfDay] = useState<HalfDay>("None");
  const [note, setNote] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  const targetId = staffId ?? (manager ? (me.data?.staffMemberId ?? staff.data?.[0]?.id ?? null) : (me.data?.staffMemberId ?? null));
  const leaveTypeId = typeId ?? types.data?.[0]?.id ?? null;
  const half = start === end ? halfDay : "None";
  const valid = !!targetId && !!leaveTypeId && end >= start;
  const preview = useQuery({
    queryKey: [...leaveKey, "preview", targetId, leaveTypeId, start, end, half],
    queryFn: () =>
      unwrap(
        api.POST("/api/leave-requests/preview", {
          body: { staffMemberId: targetId, leaveTypeId: leaveTypeId!, startDate: start, endDate: end, halfDay: half },
        }),
      ),
    enabled: valid,
  });

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/leave-requests", {
          body: { staffMemberId: targetId, leaveTypeId: leaveTypeId!, startDate: start, endDate: end, halfDay: half, note: note.trim() || null },
        }),
      ),
    onSuccess: async (created) => {
      await refresh();
      toast.success(created.status === "Approved" ? "İzin girildi; takvimde kapalı görünür." : "İzin talebiniz gönderildi. Onaylanınca bildirim gelir.");
      router.push("/panel/mesai-izin?sekme=izinler");
    },
    onError: (e) => setErrors(Object.keys(fieldErrors(e)).length ? fieldErrors(e) : { form: errorMessage(e) }),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (!leaveTypeId) next.leaveTypeId = "İzin türünü seçin.";
    if (end < start) next.endDate = "Son gün ilk günden önce olamaz.";
    setErrors(next);
    if (Object.keys(next).length === 0) save.mutate();
  };

  if (types.isPending || me.isPending || (manager && staff.isPending))
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (types.isError) return <QueryError error={types.error} onRetry={types.refetch} />;
  if (!manager && !me.data?.staffMemberId)
    return (
      <FormMessage tone="info">
        Hesabınız bir personel kaydına bağlı değil. İzin talep etmek için işletme sahibi veya yönetici Personel ekranından hesabınızı bağlamalı.
      </FormMessage>
    );
  const staffName = staff.data?.find((s) => s.id === targetId)?.fullName;

  return (
    <div className="flex max-w-xl flex-col gap-5">
      <Link href="/panel/mesai-izin?sekme=izinler" className="inline-flex min-h-11 items-center gap-2 self-start text-sm font-semibold text-black">
        <ArrowLeft aria-hidden className="size-4" />
        Mesai ve İzin
      </Link>
      <h1 className="text-2xl font-bold tracking-tight text-black">{manager ? "İzin gir" : "İzin talep et"}</h1>
      <form onSubmit={submit} noValidate className="flex flex-col gap-5">
        {manager && (
          <Field label="Personel">
            <select className={selectClass} value={targetId ?? ""} onChange={(e) => setStaffId(e.target.value)}>
              {staff.data?.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName}
                </option>
              ))}
            </select>
          </Field>
        )}
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 text-sm font-semibold text-black">İzin türü</legend>
          <ChoiceGroup label="İzin türü" options={(types.data ?? []).map((t) => ({ value: t.id, label: t.name }))} value={leaveTypeId ?? ""} onChange={setTypeId} />
          {errors.leaveTypeId && <p className="text-sm text-danger-600">{errors.leaveTypeId}</p>}
        </fieldset>
        <div className="grid grid-cols-2 gap-3">
          <Field label="İlk gün" error={errors.startDate}>
            <Input type="date" value={start} onChange={(e) => (setStart(e.target.value), end < e.target.value && setEnd(e.target.value))} />
          </Field>
          <Field label="Son gün" error={errors.endDate}>
            <Input type="date" value={end} min={start} onChange={(e) => setEnd(e.target.value)} />
          </Field>
        </div>
        {start === end && (
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-2 text-sm font-semibold text-black">Süre</legend>
            <ChoiceGroup
              label="Süre"
              options={(["None", "Morning", "Afternoon"] as HalfDay[]).map((h) => ({ value: h, label: halfDayNames[h] }))}
              value={halfDay}
              onChange={setHalfDay}
            />
          </fieldset>
        )}
        <Field label="Not (isteğe bağlı)" error={errors.note} hint="Sağlık bilgisi veya tanı yazmayın; rapor için türü seçmeniz yeterli.">
          <Textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} rows={2} />
        </Field>

        {valid && preview.data && <PreviewSummary preview={preview.data} staffName={manager ? staffName : undefined} />}
        {preview.isError && <FormMessage>{errorMessage(preview.error)}</FormMessage>}
        {errors.form && <FormMessage>{errors.form}</FormMessage>}
        <Button type="submit" size="lg" className="max-sm:w-full sm:self-start" disabled={save.isPending || (preview.data != null && preview.data.days <= 0)}>
          {manager ? "İzni kaydet" : "Talebi gönder"}
        </Button>
      </form>
    </div>
  );
}
