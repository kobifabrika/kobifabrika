"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { FileText, Plus, Search } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

import { useDebounced } from "@/components/catalog/product-list";
import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { QuoteStatusBadge, quotesKey } from "@/components/quotes/quote-bits";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, unwrap } from "@/lib/api/client";
import type { QuoteStatus } from "@/lib/api/types";
import { formatDayShort, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";

const PAGE_SIZE = 25;

const statuses: { key: QuoteStatus | "all"; label: string }[] = [
  { key: "all", label: "Tümü" },
  { key: "Sent", label: "Gönderildi" },
  { key: "Accepted", label: "Onaylandı" },
  { key: "Draft", label: "Taslak" },
  { key: "Rejected", label: "Reddedildi" },
  { key: "Converted", label: "Satışa dönüştü" },
];

/** Teklif listesi: durum süzgeci, numara/müşteri araması. Personel sadece kendi tekliflerini görür. */
export function QuoteList({ contactId }: { contactId?: string }) {
  const { role } = useSession();
  const [status, setStatus] = useState<QuoteStatus | "all">("all");
  const [search, setSearch] = useState("");
  const q = useDebounced(search.trim(), 300);
  const query = { status: status === "all" ? undefined : status, q: q || undefined, contactId };
  const list = useInfiniteQuery({
    queryKey: [...quotesKey, "list", query],
    queryFn: ({ pageParam }) => unwrap(api.GET("/api/quotes", { params: { query: { ...query, page: pageParam, pageSize: PAGE_SIZE } } })),
    initialPageParam: 1,
    getNextPageParam: (last, pages) => (pages.length * PAGE_SIZE < last.total ? pages.length + 1 : undefined),
  });
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div className="flex flex-col gap-4">
      {!contactId && (
        <div className="-mx-4 overflow-x-auto px-4 lg:mx-0 lg:px-0" role="group" aria-label="Durum">
          <div className="flex min-w-max gap-2">
            {statuses.map((s) => (
              <button
                key={s.key}
                type="button"
                aria-pressed={status === s.key}
                onClick={() => setStatus(s.key)}
                className={cn(
                  "min-h-11 rounded-full border px-4 text-sm font-medium whitespace-nowrap",
                  status === s.key ? "border-black bg-black text-white" : "border-border bg-background text-black hover:border-black",
                )}
              >
                {s.label}
              </button>
            ))}
          </div>
        </div>
      )}
      {!contactId && (
        <div className="relative">
          <Search aria-hidden className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600" />
          <Input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Teklif no veya müşteri ara"
            aria-label="Teklif ara"
            className="pl-9"
          />
        </div>
      )}

      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        status === "all" && !q ? (
          <EmptyState icon={<FileText className="size-10" />} title="Henüz teklif yok">
            {can(role, "createSales") && (
              <Button asChild>
                <Link href={contactId ? `/panel/teklifler/yeni?musteri=${contactId}` : "/panel/teklifler/yeni"}>
                  <Plus aria-hidden />
                  Teklif hazırla
                </Link>
              </Button>
            )}
          </EmptyState>
        ) : (
          <p className="text-sm text-neutral-600">Bu süzgece uyan teklif yok.</p>
        )
      ) : (
        <>
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {items.map((x) => (
              <li key={x.id}>
                <Link href={`/panel/teklifler/${x.id}`} className="flex min-h-14 items-center justify-between gap-3 py-3 hover:bg-neutral-50">
                  <span className="flex min-w-0 flex-col gap-1">
                    <span className="flex flex-wrap items-center gap-2 text-sm font-semibold text-black">
                      <span className="tabular-nums">{x.no}</span>
                      <QuoteStatusBadge status={x.status} expired={x.isExpired} />
                    </span>
                    <span className="truncate text-xs text-neutral-700">
                      {x.contactName} · {formatDayShort(x.date)} · geçerlilik {formatDayShort(x.validUntil)}
                    </span>
                  </span>
                  <span className="shrink-0 text-base font-semibold text-black tabular-nums">{formatMoney(x.total)}</span>
                </Link>
              </li>
            ))}
          </ul>
          {list.hasNextPage && (
            <Button variant="outline" className="self-center" onClick={() => list.fetchNextPage()} disabled={list.isFetchingNextPage}>
              Daha fazla göster
            </Button>
          )}
        </>
      )}
    </div>
  );
}
