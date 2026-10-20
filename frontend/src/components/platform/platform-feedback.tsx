"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ImageIcon } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";

import { ResponsivePanel } from "@/components/appointments/appointment-bits";
import { selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";
import { Textarea } from "@/components/ui/textarea";
import { API_URL, api, errorMessage, unwrap } from "@/lib/api/client";
import {
  feedbackStatusNames,
  roleNames,
  type AdminFeedbackItem,
  type FeedbackStatus,
  type Role,
} from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";

import { adminKey } from "./platform-overview";

const statuses: FeedbackStatus[] = ["New", "InProgress", "Resolved", "Closed"];

/** /platform/geri-bildirim: durum çipleri, liste; satıra dokununca ayrıntı (ekran görüntüsü, cihaz, durum ve not). */
export function PlatformFeedback() {
  const [status, setStatus] = useState<FeedbackStatus | "">("New");
  const [open, setOpen] = useState<AdminFeedbackItem | null>(null);
  const query = useQuery({
    queryKey: [...adminKey, "feedback", status],
    queryFn: () => unwrap(api.GET("/api/admin/feedback", { params: { query: { status: status || undefined } } })),
  });

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold tracking-tight text-black">Geri bildirim</h1>
      <ScrollRow aria-label="Durum">
        <Chip active={status === ""} onClick={() => setStatus("")}>
          Hepsi
        </Chip>
        {statuses.map((s) => (
          <Chip key={s} active={status === s} onClick={() => setStatus(s)}>
            {feedbackStatusNames[s]}
          </Chip>
        ))}
      </ScrollRow>
      {query.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : query.isError ? (
        <QueryError error={query.error} onRetry={query.refetch} />
      ) : query.data.items.length === 0 ? (
        <p className="text-sm text-neutral-600">Geri bildirim yok.</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {query.data.items.map((f) => (
            <li key={f.id}>
              <button
                type="button"
                onClick={() => setOpen(f)}
                className="flex min-h-14 w-full flex-col gap-1 py-3 text-left hover:bg-neutral-50"
              >
                <span className="flex flex-wrap items-center gap-x-2 text-sm font-semibold text-black">
                  {f.businessName}
                  <span className="font-normal text-neutral-600">· {feedbackStatusNames[f.status]}</span>
                  {f.hasScreenshot && (
                    <ImageIcon aria-label="Ekran görüntüsü var" className="size-4 text-neutral-600" />
                  )}
                </span>
                <span className="line-clamp-2 text-sm text-neutral-700">{f.body}</span>
                <span className="text-xs text-neutral-600">
                  {f.userName} · {formatDateTime(f.createdAt)}
                  {f.path && ` · ${f.path}`}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {open && <FeedbackDetail key={open.id} item={open} onClose={() => setOpen(null)} />}
    </div>
  );
}

function FeedbackDetail({ item, onClose }: { item: AdminFeedbackItem; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<FeedbackStatus>(item.status);
  const [note, setNote] = useState(item.adminNote ?? "");
  const save = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/admin/feedback/{id}", {
          params: { path: { id: item.id } },
          body: { status, adminNote: note.trim() || null },
        }),
      ),
    onSuccess: async () => {
      toast.success("Kaydedildi. Kullanıcıya bildirim gitti.");
      await queryClient.invalidateQueries({ queryKey: [...adminKey, "feedback"] });
      onClose();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const shot = `${API_URL}/api/admin/feedback/${item.id}/screenshot`;
  return (
    <ResponsivePanel
      open
      onOpenChange={(v) => !v && onClose()}
      title={item.businessName}
      description={formatDateTime(item.createdAt)}
      wide
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm whitespace-pre-line text-black">{item.body}</p>
        <dl className="grid gap-1 text-xs text-neutral-700">
          <div>
            <dt className="inline text-neutral-600">Gönderen: </dt>
            <dd className="inline">
              {item.userName}
              {item.userEmail && ` · ${item.userEmail}`}
              {item.role && ` · ${roleNames[item.role as Role] ?? item.role}`}
            </dd>
          </div>
          {item.path && (
            <div>
              <dt className="inline text-neutral-600">Ekran: </dt>
              <dd className="inline">{item.path}</dd>
            </div>
          )}
          {item.viewport && (
            <div>
              <dt className="inline text-neutral-600">Ekran boyutu: </dt>
              <dd className="inline">{item.viewport}</dd>
            </div>
          )}
          {item.userAgent && (
            <div>
              <dt className="inline text-neutral-600">Tarayıcı: </dt>
              <dd className="inline break-all">{item.userAgent}</dd>
            </div>
          )}
        </dl>
        {item.hasScreenshot && (
          <a href={shot} target="_blank" rel="noopener" className="block border border-border">
            {/* Görüntü sadece admin ucundan, oturum çerezliyle gelir; next/image optimizasyonu kullanılamaz. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={shot}
              alt={`${item.businessName} geri bildirim ekran görüntüsü`}
              className="max-h-96 w-full object-contain"
            />
          </a>
        )}
        <Link
          href={`/platform/isletmeler/${item.businessId}`}
          className="inline-flex min-h-11 items-center text-sm font-semibold text-black underline"
        >
          İşletmeye git
        </Link>
        <Field label="Durum">
          <select className={selectClass} value={status} onChange={(e) => setStatus(e.target.value as FeedbackStatus)}>
            {statuses.map((s) => (
              <option key={s} value={s}>
                {feedbackStatusNames[s]}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Not (kullanıcıya bildirimde görünmez)">
          <Textarea rows={3} maxLength={2000} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
        <Button onClick={() => save.mutate()} disabled={save.isPending}>
          Kaydet
        </Button>
      </div>
    </ResponsivePanel>
  );
}
