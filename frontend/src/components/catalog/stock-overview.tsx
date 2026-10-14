"use client";

import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { Boxes, Plus } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";

import { StockBadge, productsKey, stockKey, useStockModule } from "@/components/catalog/catalog-bits";
import { MovementList } from "@/components/catalog/product-detail";
import { StockDialog } from "@/components/catalog/stock-dialog";
import { EmptyState, Notice } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { api, unwrap } from "@/lib/api/client";
import { can } from "@/lib/permissions";
import { Chip, ScrollRow } from "@/components/ui/scroll-row";

const PAGE_SIZE = 25;
type Filter = "tum" | "kritik" | "eksi";

/** Stok ekranı: izlenen ürünler (kritik/eksi süzgeci, `?filtre=`), hızlı stok hareketi, son hareketler. */
export function StockOverview() {
  const { role } = useSession();
  const stockModule = useStockModule();
  const router = useRouter();
  const params = useSearchParams();
  const filter: Filter = params.get("filtre") === "kritik" ? "kritik" : params.get("filtre") === "eksi" ? "eksi" : "tum";
  const [dialogOpen, setDialogOpen] = useState(false);

  const summary = useQuery({ queryKey: [...stockKey, "summary"], queryFn: () => unwrap(api.GET("/api/stock/summary")) });
  const stock = filter === "kritik" ? "low" : filter === "eksi" ? "negative" : "tracked";
  const list = useInfiniteQuery({
    queryKey: [...productsKey, "stock", stock],
    queryFn: ({ pageParam }) =>
      unwrap(api.GET("/api/products", { params: { query: { stock, sort: "stock", page: pageParam, pageSize: PAGE_SIZE } } })),
    initialPageParam: 1,
    getNextPageParam: (last, pages) => (pages.length * PAGE_SIZE < last.total ? pages.length + 1 : undefined),
    enabled: stockModule,
  });
  const movements = useQuery({
    queryKey: [...stockKey, "movements", "recent"],
    queryFn: () => unwrap(api.GET("/api/stock/movements", { params: { query: { pageSize: 10 } } })),
    enabled: stockModule,
  });

  if (!stockModule)
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-2xl font-bold tracking-tight text-black">Stok</h1>
        <Notice>
          Stok modülü kapalı.{" "}
          {can(role, "manageBusiness") && (
            <Link href="/panel/ayarlar" className="font-semibold underline">
              Ayarlar → Modüller
            </Link>
          )}{" "}
          bölümünden açabilirsiniz.
        </Notice>
      </div>
    );

  const items = list.data?.pages.flatMap((p) => p.items) ?? [];
  const chips: { key: Filter; label: string; count?: number }[] = [
    { key: "tum", label: "Tümü", count: summary.data?.tracked },
    { key: "kritik", label: "Kritik", count: summary.data?.low },
    { key: "eksi", label: "Eksi stok", count: summary.data?.negative },
  ];

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-black">Stok</h1>
        {can(role, "manageCatalog") && (
          <Button onClick={() => setDialogOpen(true)}>
            <Plus aria-hidden />
            Stok hareketi
          </Button>
        )}
      </div>

      <ScrollRow role="group" aria-label="Süzgeç">
        {chips.map((c) => (
          <Chip
            key={c.key}
            active={filter === c.key}
            onClick={() => router.replace(c.key === "tum" ? "/panel/stok" : `/panel/stok?filtre=${c.key}`, { scroll: false })}
          >
            {c.label}
            {c.count != null && <span className="ml-1 tabular-nums">({c.count})</span>}
          </Chip>
        ))}
      </ScrollRow>

      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        filter === "tum" ? (
          <EmptyState icon={<Boxes className="size-10" />} title="Stoğu izlenen ürün yok">
            <p className="text-sm text-neutral-700">Ürün eklerken &quot;Stok takibi&quot;ni açın veya Excel&apos;den stokla birlikte aktarın.</p>
          </EmptyState>
        ) : (
          <p className="text-sm text-neutral-600">{filter === "kritik" ? "Kritik seviyede ürün yok." : "Eksi stokta ürün yok."}</p>
        )
      ) : (
        <>
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {items.map((p) => (
              <li key={p.id}>
                <Link href={`/panel/urunler/${p.id}`} className="flex min-h-12 items-center justify-between gap-3 py-3 hover:bg-neutral-50">
                  <span className="flex min-w-0 flex-col">
                    <span className="truncate text-sm font-semibold text-black">{p.name}</span>
                    {p.barcode && <span className="text-xs text-neutral-600 tabular-nums">{p.barcode}</span>}
                  </span>
                  <StockBadge product={p} />
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

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold text-black">Son hareketler</h2>
        {movements.isError ? (
          <QueryError error={movements.error} onRetry={movements.refetch} />
        ) : movements.data?.items.length === 0 ? (
          <p className="text-sm text-neutral-600">Henüz hareket yok.</p>
        ) : (
          movements.data && <MovementList items={movements.data.items} showProduct />
        )}
      </section>

      {dialogOpen && <StockDialog open onOpenChange={setDialogOpen} />}
    </div>
  );
}
