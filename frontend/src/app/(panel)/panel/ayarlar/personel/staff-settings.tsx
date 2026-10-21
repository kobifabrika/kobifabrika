"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { useState, type FormEvent, type ReactNode } from "react";
import { toast } from "sonner";

import { ResponsivePanel } from "@/components/appointments/appointment-bits";
import { leaveKey, useLeaveTypes } from "@/components/attendance/attendance-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { NumberInput } from "@/components/ui/number-input";
import { parseNumberText } from "@/lib/number-input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { LEGAL_NOTE, type LeaveType } from "@/lib/api/types";
import { can } from "@/lib/permissions";

const settingsKey = ["staff-settings"] as const;

function SettingsCard({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">{title}</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

/** Ayarlar → Personel: haftalık fazla mesai eşiği ve izin türleri (Sahip/Yönetici değiştirir, Mali Müşavir görür). */
export function StaffSettings() {
  const { role } = useSession();
  const readOnly = !can(role, "manageStaffTime");
  return (
    <div className="flex flex-col gap-4">
      {role === "Accountant" && <p className="text-sm text-neutral-600">Mali Müşavir olarak bu ayarları görüntüleyebilirsiniz; değiştiremezsiniz.</p>}
      <OvertimeCard readOnly={readOnly} />
      <LeaveTypesCard readOnly={readOnly} />
    </div>
  );
}

function OvertimeCard({ readOnly }: { readOnly: boolean }) {
  const queryClient = useQueryClient();
  const settings = useQuery({ queryKey: settingsKey, queryFn: () => unwrap(api.GET("/api/business/staff-settings")) });
  const [hours, setHours] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: (overtimeWeeklyHours: number) => unwrap(api.PUT("/api/business/staff-settings", { body: { overtimeWeeklyHours } })),
    onSuccess: async (next) => {
      queryClient.setQueryData(settingsKey, next);
      await queryClient.invalidateQueries({ queryKey: ["time-clock"] });
      setHours(null);
      setError(null);
      toast.success("Fazla mesai eşiği kaydedildi.");
    },
    onError: (e) => setError(fieldErrors(e).overtimeWeeklyHours ?? errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const value = parseNumberText(hours)?.value ?? Number.NaN;
    if (!Number.isFinite(value) || value < 1 || value > 80 || value * 2 !== Math.floor(value * 2))
      return setError("Haftalık eşik 1 ile 80 saat arasında, yarım saat adımlarıyla olmalı.");
    save.mutate(value);
  };

  return (
    <SettingsCard title="Fazla mesai" description="Haftalık (Pazartesi-Pazar) bu süreyi aşan çalışma fazla mesai sayılır. İş Kanunu'nda 45 saat.">
      {settings.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : settings.isError ? (
        <QueryError error={settings.error} onRetry={settings.refetch} />
      ) : (
        <form onSubmit={submit} noValidate className="flex flex-col gap-3">
          <div className="flex flex-wrap items-end gap-3">
            <Field label="Haftalık eşik (saat)" error={error ?? undefined} className="w-44">
              <NumberInput
                value={hours ?? settings.data.overtimeWeeklyHours.toLocaleString("tr-TR")}
                onChange={(e) => setHours(e.target.value)}
                disabled={readOnly}
              />
            </Field>
            {!readOnly && (
              <Button type="submit" variant="outline" disabled={save.isPending || hours === null}>
                Kaydet
              </Button>
            )}
          </div>
          <p className="text-xs text-neutral-600">{LEGAL_NOTE}</p>
        </form>
      )}
    </SettingsCard>
  );
}

function LeaveTypesCard({ readOnly }: { readOnly: boolean }) {
  const types = useLeaveTypes(true);
  const [editing, setEditing] = useState<LeaveType | "new" | null>(null);
  return (
    <SettingsCard title="İzin türleri" description="Yıllık izin bakiyeden düşer. Kullanılmış tür silinmez, pasife alınır.">
      {types.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : types.isError ? (
        <QueryError error={types.error} onRetry={types.refetch} />
      ) : (
        <div className="flex flex-col gap-3">
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {types.data.map((t) => (
              <li key={t.id} className="flex min-h-12 items-center justify-between gap-3 py-1">
                <span className="flex flex-wrap items-center gap-2 text-sm text-black">
                  <span className="font-semibold">{t.name}</span>
                  <span className="text-neutral-600">{t.isPaid ? "Ücretli" : "Ücretsiz"}</span>
                  {t.isAnnual && <Badge variant="outline">Bakiyeden düşer</Badge>}
                  {!t.isActive && <Badge variant="outline">Pasif</Badge>}
                </span>
                {!readOnly && (
                  <Button variant="ghost" size="icon" aria-label={`${t.name} türünü düzenle`} onClick={() => setEditing(t)}>
                    <Pencil aria-hidden />
                  </Button>
                )}
              </li>
            ))}
          </ul>
          {!readOnly && (
            <Button variant="outline" className="self-start" onClick={() => setEditing("new")}>
              <Plus aria-hidden />
              Tür ekle
            </Button>
          )}
        </div>
      )}
      {editing && <LeaveTypeDialog type={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} />}
    </SettingsCard>
  );
}

function LeaveTypeDialog({ type, onClose }: { type?: LeaveType; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(type?.name ?? "");
  const [isPaid, setIsPaid] = useState(type?.isPaid ?? true);
  const [isActive, setIsActive] = useState(type?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);
  const done = async (message: string) => {
    await queryClient.invalidateQueries({ queryKey: leaveKey });
    toast.success(message);
    onClose();
  };
  const save = useMutation({
    mutationFn: () =>
      unwrap(
        type
          ? api.PUT("/api/leave-types/{id}", { params: { path: { id: type.id } }, body: { name: name.trim(), isPaid, isActive } })
          : api.POST("/api/leave-types", { body: { name: name.trim(), isPaid, isActive } }),
      ),
    onSuccess: () => done(type ? "İzin türü kaydedildi." : "İzin türü eklendi."),
    onError: (e) => setError(fieldErrors(e).name ?? errorMessage(e)),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/leave-types/{id}", { params: { path: { id: type!.id } } })),
    onSuccess: () => done("İzin türü silindi."),
    onError: (e) => setError(errorMessage(e)),
  });

  return (
    <ResponsivePanel open onOpenChange={(o) => !o && onClose()} title={type ? "İzin türünü düzenle" : "İzin türü ekle"}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (!name.trim()) return setError("İzin türünün adını girin.");
          save.mutate();
        }}
        noValidate
        className="flex flex-col gap-4"
      >
        <Field label="Ad">
          <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={50} placeholder="Ör. Babalık izni" />
        </Field>
        <label className="flex min-h-11 items-center justify-between gap-3 text-sm text-black">
          Ücretli izin
          <Switch checked={isPaid} onCheckedChange={setIsPaid} />
        </label>
        {type && !type.isAnnual && (
          <label className="flex min-h-11 items-center justify-between gap-3 text-sm text-black">
            Aktif (talep formunda görünür)
            <Switch checked={isActive} onCheckedChange={setIsActive} />
          </label>
        )}
        {error && <FormMessage>{error}</FormMessage>}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={save.isPending}>
            Kaydet
          </Button>
          {type && !type.isAnnual && (
            <Button
              type="button"
              variant="ghost"
              className="text-danger-600"
              disabled={remove.isPending}
              onClick={() => window.confirm(`${type.name} silinsin mi?`) && remove.mutate()}
            >
              <Trash2 aria-hidden />
              Sil
            </Button>
          )}
        </div>
      </form>
    </ResponsivePanel>
  );
}
