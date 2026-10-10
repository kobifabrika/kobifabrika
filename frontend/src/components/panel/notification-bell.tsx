"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell } from "lucide-react";
import { cn } from "cn";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { api, unwrap } from "@/lib/api/client";
import type { NotificationItem } from "@/lib/api/types";
import { formatRelative } from "@/lib/format";

const countKey = ["notifications", "unread-count"] as const;
const listKey = ["notifications", "list"] as const;

/** Bildirim zili: okunmamış sayısı (60 sn'de bir yenilenir), son 20 bildirim, okundu işaretleme. */
export function NotificationBell() {
  const [open, setOpen] = useState(false);
  const router = useRouter();
  const queryClient = useQueryClient();

  const count = useQuery({
    queryKey: countKey,
    queryFn: () => unwrap(api.GET("/api/notifications/unread-count")),
    refetchInterval: 60_000,
  });

  const list = useQuery({
    queryKey: listKey,
    queryFn: () => unwrap(api.GET("/api/notifications", { params: { query: { page: 1, pageSize: 20 } } })),
    enabled: open,
  });

  const refresh = () => Promise.all([queryClient.invalidateQueries({ queryKey: countKey }), queryClient.invalidateQueries({ queryKey: listKey })]);

  const markRead = useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/notifications/{id}/read", { params: { path: { id } } })),
    onSettled: refresh,
  });

  const markAll = useMutation({
    mutationFn: () => unwrap(api.POST("/api/notifications/read-all")),
    onSettled: refresh,
  });

  const unread = count.data?.count ?? 0;

  const openItem = (n: NotificationItem) => {
    if (!n.readAt) markRead.mutate(n.id);
    setOpen(false);
    if (n.link) router.push(n.link);
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="relative text-brand-900"
          aria-label={unread > 0 ? `Bildirimler, ${unread} okunmamış` : "Bildirimler"}
        >
          <Bell aria-hidden className="size-5" />
          {unread > 0 && (
            <span
              aria-hidden
              className="absolute top-1 right-1 flex min-w-5 items-center justify-center rounded-full bg-danger-600 px-1 text-xs leading-5 font-bold text-white tabular-nums"
            >
              {unread > 99 ? "99+" : unread}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-[calc(100vw-2rem)] max-w-sm p-0">
        <div className="flex items-center justify-between gap-2 border-b border-border px-4 py-2">
          <p className="text-sm font-semibold text-brand-900">Bildirimler</p>
          <Button
            variant="ghost"
            className="text-brand-500"
            disabled={unread === 0 || markAll.isPending}
            onClick={() => markAll.mutate()}
          >
            Tümünü okundu işaretle
          </Button>
        </div>
        <div className="max-h-[60vh] overflow-y-auto">
          {list.isPending ? (
            <p role="status" className="px-4 py-6 text-center text-sm text-neutral-600">
              Yükleniyor…
            </p>
          ) : !list.data?.items.length ? (
            <p className="px-4 py-6 text-center text-sm text-neutral-600">Henüz bildirim yok.</p>
          ) : (
            <ul>
              {list.data.items.map((n) => (
                <li key={n.id} className="border-b border-border last:border-0">
                  <button
                    type="button"
                    onClick={() => openItem(n)}
                    className={cn(
                      "flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-neutral-50",
                      !n.readAt && "bg-neutral-50",
                    )}
                  >
                    <span
                      aria-hidden
                      className={cn("mt-1.5 size-2 shrink-0 rounded-full", n.readAt ? "bg-transparent" : "bg-brand-500")}
                    />
                    <span className="flex flex-col gap-0.5">
                      <span className={cn("text-sm text-brand-900", !n.readAt && "font-semibold")}>
                        {n.title}
                        {!n.readAt && <span className="sr-only"> (okunmadı)</span>}
                      </span>
                      {n.body && <span className="text-sm text-neutral-600">{n.body}</span>}
                      <span className="text-xs text-neutral-600">{formatRelative(n.createdAt)}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
}
