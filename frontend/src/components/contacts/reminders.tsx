"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { BellRing, Check, Plus, RotateCcw, Trash2 } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { EmptyState, selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { Reminder } from "@/lib/api/types";
import { formatDateTime, istanbulParts, istanbulToUtcIso, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";
import { useIsDesktop } from "@/lib/use-media";

const remindersKey = ["reminders"] as const;

type Scope = "mine" | "all";

/**
 * Hatırlatmalar. contactId verilirse o müşterinin (herkes görür), yoksa "benim" veya (Sahip/Yönetici) "tümü".
 * Vakti gelince atanan kişiye bildirim düşer (sunucuda dakikada bir kontrol).
 */
export function ReminderList({ contactId, contactName }: { contactId?: string; contactName?: string }) {
  const { role } = useSession();
  const [scope, setScope] = useState<Scope>("mine");
  const [status, setStatus] = useState<"open" | "done">("open");
  const [editing, setEditing] = useState<Reminder | "new" | null>(null);
  const managers = can(role, "manageContacts");
  const write = can(role, "writeContacts");

  const reminders = useQuery({
    queryKey: [...remindersKey, contactId ?? scope, status],
    queryFn: () => unwrap(api.GET("/api/reminders", { params: { query: { scope, status, contactId } } })),
  });

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <ScrollRow role="group" aria-label="Hatırlatma süzgeci" className="min-w-0 flex-1">
          {!contactId && managers && (
            <>
              <Chip active={scope === "mine"} onClick={() => setScope("mine")}>
                Bana atananlar
              </Chip>
              <Chip active={scope === "all"} onClick={() => setScope("all")}>
                Tümü
              </Chip>
            </>
          )}
          <Chip active={status === "open"} onClick={() => setStatus("open")}>
            Açık
          </Chip>
          <Chip active={status === "done"} onClick={() => setStatus("done")}>
            Tamamlanan
          </Chip>
        </ScrollRow>
        {write && (
          <Button onClick={() => setEditing("new")}>
            <Plus aria-hidden />
            Hatırlatma ekle
          </Button>
        )}
      </div>

      {reminders.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : reminders.isError ? (
        <QueryError error={reminders.error} onRetry={reminders.refetch} />
      ) : reminders.data.length === 0 ? (
        <EmptyState
          icon={<BellRing className="size-10" />}
          title={status === "open" ? "Açık hatırlatma yok" : "Tamamlanan hatırlatma yok"}
        >
          {status === "open" && (
            <p className="text-sm text-neutral-600">
              ör. &quot;Boya için 3 hafta sonra ara&quot;, &quot;Ödemeyi sor&quot;.
            </p>
          )}
        </EmptyState>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {reminders.data.map((r) => (
            <ReminderRow key={r.id} reminder={r} showContact={!contactId} onEdit={() => setEditing(r)} />
          ))}
        </ul>
      )}

      {editing && (
        <ReminderDialog
          reminder={editing === "new" ? null : editing}
          contactId={contactId}
          contactName={contactName}
          onClose={() => setEditing(null)}
        />
      )}
    </div>
  );
}


function ReminderRow({
  reminder: r,
  showContact,
  onEdit,
}: {
  reminder: Reminder;
  showContact: boolean;
  onEdit: () => void;
}) {
  const { me, role } = useSession();
  const queryClient = useQueryClient();
  const editable = can(role, "manageContacts") || r.assignedUserId === me.user.id;
  const write = can(role, "writeContacts") && editable;

  const act = useMutation({
    mutationFn: (kind: "done" | "reopen" | "delete") =>
      unwrap(
        kind === "delete"
          ? api.DELETE("/api/reminders/{id}", { params: { path: { id: r.id } } })
          : kind === "done"
            ? api.POST("/api/reminders/{id}/done", { params: { path: { id: r.id } } })
            : api.POST("/api/reminders/{id}/reopen", { params: { path: { id: r.id } } }),
      ),
    onSuccess: async (_, kind) => {
      toast.success(kind === "done" ? "Tamamlandı." : kind === "reopen" ? "Tekrar açıldı." : "Silindi.");
      await queryClient.invalidateQueries({ queryKey: remindersKey });
      await queryClient.invalidateQueries({ queryKey: ["timeline"] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <li className={cn("flex items-center gap-3 py-3", r.isOverdue && "border-l-2 border-brand pl-3")}>
      {write && !r.doneAt && (
        <Button
          variant="outline"
          size="icon"
          aria-label="Tamamlandı"
          onClick={() => act.mutate("done")}
          disabled={act.isPending}
        >
          <Check aria-hidden />
        </Button>
      )}
      <button
        type="button"
        onClick={write ? onEdit : undefined}
        disabled={!write}
        className="flex min-h-11 min-w-0 flex-1 flex-col items-start text-left"
      >
        <span className={cn("text-sm font-semibold text-black", r.doneAt && "line-through decoration-neutral-400")}>
          {r.title}
        </span>
        <span className="text-xs text-neutral-600">
          {r.isOverdue ? "Vakti geçti · " : ""}
          {formatDateTime(r.dueAt)} · {r.assignedUserName}
        </span>
      </button>
      {showContact && r.contactId && (
        <Link
          href={`/panel/musteriler/${r.contactId}?sekme=hatirlatmalar`}
          className="hidden text-sm font-semibold text-black underline decoration-brand underline-offset-2 sm:inline"
        >
          {r.contactName}
        </Link>
      )}
      {write && r.doneAt && (
        <Button variant="ghost" size="icon" aria-label="Tekrar aç" onClick={() => act.mutate("reopen")}>
          <RotateCcw aria-hidden />
        </Button>
      )}
      {write && (
        <Button
          variant="ghost"
          size="icon"
          aria-label="Sil"
          onClick={() => window.confirm("Hatırlatma silinsin mi?") && act.mutate("delete")}
        >
          <Trash2 aria-hidden />
        </Button>
      )}
    </li>
  );
}

function ReminderDialog({
  reminder,
  contactId,
  contactName,
  onClose,
}: {
  reminder: Reminder | null;
  contactId?: string;
  contactName?: string;
  onClose: () => void;
}) {
  // Telefonda pencere açılınca klavye açılıp düğmeleri örtmesin; masaüstünde ilk alana odaklanılır.
  const isDesktop = useIsDesktop();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const initial = reminder ? istanbulParts(reminder.dueAt) : { date: todayIstanbul(), time: "10:00" };
  const [title, setTitle] = useState(reminder?.title ?? "");
  const [date, setDate] = useState(initial.date);
  const [time, setTime] = useState(initial.time);
  const [assignee, setAssignee] = useState(reminder?.assignedUserId ?? me.user.id);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const assignees = useQuery({
    queryKey: ["reminders", "assignees"],
    queryFn: () => unwrap(api.GET("/api/reminders/assignees")),
  });

  const save = useMutation({
    mutationFn: () => {
      const body = {
        title: title.trim(),
        dueAt: istanbulToUtcIso(date, time),
        contactId: reminder?.contactId ?? contactId ?? null,
        assignedUserId: assignee,
      };
      return unwrap(
        reminder
          ? api.PUT("/api/reminders/{id}", { params: { path: { id: reminder.id } }, body })
          : api.POST("/api/reminders", { body }),
      );
    },
    onSuccess: async () => {
      toast.success(reminder ? "Hatırlatma güncellendi." : "Hatırlatma eklendi.");
      await queryClient.invalidateQueries({ queryKey: remindersKey });
      await queryClient.invalidateQueries({ queryKey: ["timeline"] });
      onClose();
    },
    onError: (e) => {
      const f = fieldErrors(e);
      setErrors(Object.keys(f).length ? f : { form: errorMessage(e) });
    },
  });

  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{reminder ? "Hatırlatmayı düzenle" : "Hatırlatma ekle"}</DialogTitle>
          <DialogDescription>
            {contactName ?? reminder?.contactName ?? "Vakti gelince seçtiğiniz kişiye bildirim düşer."}
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4">
          <Field label="Ne hatırlatılsın?" error={errors.title}>
            <Input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={200}
              placeholder="ör. Boya için ara"
              autoFocus={isDesktop}
            />
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Gün" error={errors.dueAt}>
              <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
            <Field label="Saat">
              <Input type="time" value={time} onChange={(e) => setTime(e.target.value)} />
            </Field>
          </div>
          <Field label="Kime?">
            <select className={selectClass} value={assignee} onChange={(e) => setAssignee(e.target.value)}>
              {assignees.data?.map((a) => (
                <option key={a.userId} value={a.userId}>
                  {a.userId === me.user.id ? `${a.fullName} (ben)` : a.fullName}
                </option>
              )) ?? <option value={me.user.id}>Ben</option>}
            </select>
          </Field>
          {errors.form && <FormMessage>{errors.form}</FormMessage>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>
            Vazgeç
          </Button>
          <Button
            onClick={() => {
              if (!title.trim()) return setErrors({ title: "Ne hatırlatılsın? Bir başlık yazın." });
              if (!date || !time) return setErrors({ dueAt: "Tarih ve saat seçin." });
              save.mutate();
            }}
            disabled={save.isPending}
          >
            {save.isPending ? "Kaydediliyor…" : "Kaydet"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
