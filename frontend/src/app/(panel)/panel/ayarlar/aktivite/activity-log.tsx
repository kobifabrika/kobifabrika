"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { Field, FormMessage } from "@/components/forms/field";
import { RequirePermission } from "@/components/panel/settings-nav";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { api, errorMessage, unwrap } from "@/lib/api/client";
import type { AuditAction } from "@/lib/api/types";
import { auditActionLabels, auditDetail } from "@/lib/audit";
import { formatDateTime } from "@/lib/format";

const PAGE_SIZE = 25;

export function ActivityLog() {
  return (
    <RequirePermission permission="viewAuditLog">
      <ActivityList />
    </RequirePermission>
  );
}

function ActivityList() {
  const [page, setPage] = useState(1);
  const [action, setAction] = useState<AuditAction | "">("");

  const logs = useQuery({
    queryKey: ["audit-logs", page, action],
    queryFn: () =>
      unwrap(api.GET("/api/audit-logs", { params: { query: { page, pageSize: PAGE_SIZE, ...(action ? { action } : {}) } } })),
    placeholderData: keepPreviousData,
  });

  const total = logs.data?.total ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-neutral-600">Girişler, davetler, rol değişiklikleri ve işletme ayarı değişiklikleri burada kayıtlıdır.</p>
      <Field label="İşlem türü" className="sm:w-72">
        <select
          className="h-11 w-full rounded-lg border border-input bg-background px-3 text-base text-brand-900"
          value={action}
          onChange={(e) => {
            setAction(e.target.value as AuditAction | "");
            setPage(1);
          }}
        >
          <option value="">Tümü</option>
          {Object.entries(auditActionLabels).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </Field>

      {logs.isError ? (
        <FormMessage>{errorMessage(logs.error)}</FormMessage>
      ) : (
        <Card className="py-0">
          <CardContent className="px-0">
            {logs.isPending ? (
              <p role="status" className="px-4 py-6 text-sm text-neutral-600">Yükleniyor…</p>
            ) : logs.data.items.length === 0 ? (
              <p className="px-4 py-6 text-center text-sm text-neutral-600">Kayıt yok.</p>
            ) : (
              <ul className="flex flex-col divide-y divide-border">
                {logs.data.items.map((item) => {
                  const detail = auditDetail(item);
                  return (
                    <li key={item.id} className="flex flex-col gap-1 px-4 py-3 sm:flex-row sm:items-start sm:gap-4">
                      <time dateTime={item.at} className="shrink-0 text-xs text-neutral-600 tabular-nums sm:w-40 sm:pt-0.5">
                        {formatDateTime(item.at)}
                      </time>
                      <div className="flex flex-col gap-0.5">
                        <p className="text-sm text-brand-900">
                          <span className="font-semibold">{item.userName ?? "Sistem"}</span> {auditActionLabels[item.action].toLocaleLowerCase("tr-TR")}
                        </p>
                        {detail && <p className="text-sm break-all text-neutral-600">{detail}</p>}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </CardContent>
        </Card>
      )}

      {pages > 1 && (
        <div className="flex items-center justify-between gap-2">
          <Button variant="outline" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Önceki
          </Button>
          <span className="text-sm text-neutral-600 tabular-nums">
            {page} / {pages}
          </span>
          <Button variant="outline" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>
            Sonraki
          </Button>
        </div>
      )}
    </div>
  );
}
