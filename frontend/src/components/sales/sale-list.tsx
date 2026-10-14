"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import { cn } from "cn";
import { Download, Plus, ReceiptText, Search } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

import { useDebounced } from "@/components/catalog/product-list";
import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { salesKey } from "@/components/sales/quick-sale";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { API_URL, api, unwrap } from "@/lib/api/client";
import { paymentMethodNames, type SaleListItem } from "@/lib/api/types";
import { formatMoney, istanbulParts, todayIstanbul } from "@/lib/format";
import { can } from "@/lib/permissions";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";

const PAGE_SIZE = 25;
type Range = "today" | "week" | "month" | "all";

/** İstanbul gününe göre aralık başlangıcı: bugün, bu hafta (Pazartesi), bu ay. */
export function rangeStart(range: Range, today = todayIstanbul()): string | undefined {
  if (range === "all") return undefined;
  if (range === "today") return today;
  if (range === "month") return `${today.slice(0, 8)}01`;
  const d = new Date(`${today}T00:00:00Z`);
  const offset = (d.getUTCDay() + 6) % 7;
  d.setUTCDate(d.getUTCDate() - offset);
  return d.toISOString().slice(0, 10);
}

const ranges: { key: Range; label: string }[] = [
  { key: "today", label: "Bugün" },
  { key: "week", label: "Bu hafta" },
  { key: "month", label: "Bu ay" },
  { key: "all", label: "Tümü" },
];

/** Satış listesi: dönem, arama, iptal süzgeci, dönem toplamı. Personel sadece kendi satışlarını görür ("Satışlarım"). */
export function SaleList({ contactId }: { contactId?: string }) {
  const { role } = useSession();
  const [range, setRange] = useState<Range>(contactId ? "all" : "today");
  const [cancelled, setCancelled] = useState(false);
  const [search, setSearch] = useState("");
  const q = useDebounced(search.trim(), 300);
  const own = !can(role, "viewAllSales");

  const query = {
    from: rangeStart(range),
    q: q || undefined,
    status: cancelled ? ("Cancelled" as const) : undefined,
    contactId,
  };
  const list = useInfiniteQuery({
    queryKey: [...salesKey, "list", query],
    queryFn: ({ pageParam }) => unwrap(api.GET("/api/sales", { params: { query: { ...query, page: pageParam, pageSize: PAGE_SIZE } } })),
    initialPageParam: 1,
    getNextPageParam: (last, pages) => (pages.length * PAGE_SIZE < last.total ? pages.length + 1 : undefined),
  });
  const first = list.data?.pages[0];
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];
  const exportHref = `${API_URL}/api/sales/export.xlsx?${new URLSearchParams(
    Object.entries({ from: query.from, q: query.q, status: query.status }).filter(([, v]) => v) as [string, string][],
  )}`;

  return (
    <div className="flex flex-col gap-4">
      {!contactId && (
        <div className="flex flex-wrap items-center justify-between gap-2">
          <ScrollRow role="group" aria-label="Dönem" className="min-w-0 flex-1">
            {ranges.map((r) => (
              <Chip key={r.key} active={range === r.key} onClick={() => setRange(r.key)}>
                {r.label}
              </Chip>
            ))}
          </ScrollRow>
          {can(role, "exportData") && (
            <Button asChild variant="outline">
              <a href={exportHref} download>
                <Download aria-hidden />
                <span className="sr-only sm:not-sr-only">Excel&apos;e aktar</span>
              </a>
            </Button>
          )}
        </div>
      )}

      {first && (
        <div className="flex flex-wrap items-end gap-x-6 gap-y-1 border-t-2 border-black pt-2">
          <div className="flex flex-col">
            <span className="text-xs text-neutral-600">{own ? "Satışlarım" : "Satış toplamı"}</span>
            <span className="text-2xl font-bold text-black tabular-nums">{formatMoney(first.sum)}</span>
          </div>
          <span className="pb-1 text-sm text-neutral-700 tabular-nums">{first.count} satış</span>
        </div>
      )}

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search aria-hidden className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600" />
          <Input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Satış no veya müşteri ara"
            aria-label="Satış ara"
            className="pl-9"
          />
        </div>
        <label className="flex min-h-11 items-center gap-2 text-sm text-black">
          <input type="checkbox" className="size-5 accent-black" checked={cancelled} onChange={(e) => setCancelled(e.target.checked)} />
          Sadece iptaller
        </label>
      </div>

      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        !q && !cancelled && range === "today" && !contactId ? (
          <EmptyState icon={<ReceiptText className="size-10" />} title="Bugün henüz satış yok">
            {can(role, "createSales") && (
              <Button asChild>
                <Link href="/panel/satislar/yeni">
                  <Plus aria-hidden />
                  Yeni satış
                </Link>
              </Button>
            )}
          </EmptyState>
        ) : (
          <p className="text-sm text-neutral-600">Bu süzgece uyan satış yok.</p>
        )
      ) : (
        <>
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {items.map((s) => (
              <SaleRow key={s.id} sale={s} />
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

function SaleRow({ sale: s }: { sale: SaleListItem }) {
  const { date, time } = istanbulParts(s.date);
  const cancelled = s.status === "Cancelled";
  return (
    <li>
      <Link href={`/panel/satislar/${s.id}`} className="flex min-h-14 items-center justify-between gap-3 py-3 hover:bg-neutral-50">
        <span className="flex min-w-0 flex-col gap-0.5">
          <span className="flex items-center gap-2 text-sm font-semibold text-black">
            <span className="tabular-nums">{s.no}</span>
            {cancelled && <span className="rounded border border-black px-1.5 text-xs font-semibold">İptal</span>}
          </span>
          <span className="truncate text-xs text-neutral-700">
            {date.split("-").reverse().join(".")} {time}
            {s.contactName && ` · ${s.contactName}`}
            {s.methods.length > 0 && ` · ${s.methods.map((m) => paymentMethodNames[m]).join(", ")}`}
          </span>
        </span>
        <span className={cn("shrink-0 text-base font-semibold tabular-nums", cancelled ? "text-neutral-600 line-through" : "text-black")}>
          {formatMoney(s.total)}
        </span>
      </Link>
    </li>
  );
}
