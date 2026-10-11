"use client";

import { useInfiniteQuery, useMutation, useQuery } from "@tanstack/react-query";
import { ArrowLeft, MoreVertical, Pencil } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { toast } from "sonner";

import { StockBadge, productsKey, stockKey, useCatalogRefresh, useStockModule } from "@/components/catalog/catalog-bits";
import { StockDialog } from "@/components/catalog/stock-dialog";
import { Notice } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import { productTypeNames, stockMovementTypeNames, type Product, type StockMovement } from "@/lib/api/types";
import { formatDateTime, formatDayShort, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";
import { formatQuantity, round2, unitNames, vatOf } from "@/lib/sales";

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-4 border-b border-border py-3 last:border-0">
      <dt className="text-sm text-neutral-600">{label}</dt>
      <dd className="text-right text-sm font-semibold text-black tabular-nums">{children}</dd>
    </div>
  );
}

export function ProductDetail({ id }: { id: string }) {
  const { role } = useSession();
  const router = useRouter();
  const stockModule = useStockModule();
  const refresh = useCatalogRefresh();
  const [stockKind, setStockKind] = useState<"In" | "Out" | "Adjustment" | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const product = useQuery({
    queryKey: [...productsKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/products/{id}", { params: { path: { id } } })),
  });

  const setActive = useMutation({
    mutationFn: (active: boolean) =>
      unwrap(
        active
          ? api.POST("/api/products/{id}/activate", { params: { path: { id } } })
          : api.POST("/api/products/{id}/deactivate", { params: { path: { id } } }),
      ),
    onSuccess: async (p) => {
      await refresh();
      toast.success(p.isActive ? "Satışa açıldı." : "Pasife alındı; satışta görünmez.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/products/{id}", { params: { path: { id } } })),
    onSuccess: async (r) => {
      setConfirmDelete(false);
      await refresh();
      if (r.deleted) {
        toast.success("Silindi.");
        router.push("/panel/urunler");
      } else {
        toast.success("Satış veya stok geçmişi olduğu için silinmedi, pasife alındı.");
      }
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (product.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (product.isError)
    return (
      <div className="flex flex-col gap-3">
        {product.error instanceof ApiError && product.error.status === 404 ? (
          <FormMessage>Ürün bulunamadı.</FormMessage>
        ) : (
          <QueryError error={product.error} onRetry={product.refetch} />
        )}
        <Link href="/panel/urunler" className="text-sm font-semibold text-black underline">
          Listeye dön
        </Link>
      </div>
    );

  const p = product.data;
  const manage = can(role, "manageCatalog");
  const net = round2(p.grossSalePrice - vatOf(p.grossSalePrice, p.vatRate));
  const canMoveStock = manage && p.trackStock && stockModule;

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-5">
      <Link href="/panel/urunler" className="flex min-h-11 w-fit items-center gap-2 text-sm font-semibold text-neutral-700 hover:text-black">
        <ArrowLeft aria-hidden className="size-4" />
        Ürünler/Hizmetler
      </Link>

      <header className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="text-2xl font-bold tracking-tight break-words text-black">{p.name}</h1>
          <p className="flex flex-wrap gap-x-3 text-sm text-neutral-700">
            <span>{productTypeNames[p.type]}</span>
            {p.categoryName && <span>{p.categoryName}</span>}
            {!p.isActive && <span className="font-semibold text-black">Pasif</span>}
          </p>
          <p className="text-2xl font-bold text-black tabular-nums">{formatMoney(p.grossSalePrice)}</p>
          {p.trackStock && <StockBadge product={p} className="text-base" />}
        </div>
        {manage && (
          <div className="flex shrink-0 gap-2">
            <Button asChild variant="outline">
              <Link href={`/panel/urunler/${p.id}/duzenle`}>
                <Pencil aria-hidden />
                <span className="sr-only sm:not-sr-only">Düzenle</span>
              </Link>
            </Button>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="icon" aria-label="Diğer işlemler">
                  <MoreVertical aria-hidden />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem className="min-h-11" onSelect={() => setActive.mutate(!p.isActive)}>
                  {p.isActive ? "Pasife al" : "Satışa aç"}
                </DropdownMenuItem>
                <DropdownMenuItem className="min-h-11 text-danger-600" onSelect={() => setConfirmDelete(true)}>
                  Sil
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        )}
      </header>

      {p.trackStock && stockModule && p.isLowStock && (
        <Notice>Stok kritik seviyede. Sipariş vermeyi unutmayın.</Notice>
      )}

      {canMoveStock && (
        <div className="grid grid-cols-3 gap-2 sm:flex">
          <Button onClick={() => setStockKind("In")}>Stok girişi</Button>
          <Button variant="outline" onClick={() => setStockKind("Out")}>
            Çıkış
          </Button>
          <Button variant="outline" onClick={() => setStockKind("Adjustment")}>
            Sayım
          </Button>
        </div>
      )}

      <section className="flex flex-col gap-1">
        <h2 className="text-lg font-semibold text-black">Bilgiler</h2>
        <dl>
          <Row label="Satış fiyatı (KDV dahil)">{formatMoney(p.grossSalePrice)}</Row>
          <Row label={`KDV (%${p.vatRate})`}>{formatMoney(round2(p.grossSalePrice - net))}</Row>
          <Row label="KDV hariç">{formatMoney(net)}</Row>
          {p.purchasePrice != null && <Row label="Alış fiyatı">{formatMoney(p.purchasePrice)}</Row>}
          <Row label="Birim">{unitNames[p.unit]}</Row>
          {p.barcode && <Row label="Barkod">{p.barcode}</Row>}
          {p.durationMinutes != null && <Row label="Süre">{p.durationMinutes} dk</Row>}
          {p.trackStock && p.criticalStockLevel != null && (
            <Row label="Kritik seviye">{formatQuantity(p.criticalStockLevel, p.unit)}</Row>
          )}
        </dl>
        {p.notes && <p className="pt-2 text-sm text-neutral-700">{p.notes}</p>}
      </section>

      {p.trackStock && <Movements product={p} />}

      {stockKind && (
        <StockDialog key={stockKind} product={p} kind={stockKind} open onOpenChange={(v) => !v && setStockKind(null)} />
      )}

      <Dialog open={confirmDelete} onOpenChange={setConfirmDelete}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{p.name} silinsin mi?</DialogTitle>
            <DialogDescription>
              Satış, teklif veya stok geçmişi varsa ürün silinmez; pasife alınır ve satışta görünmez.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmDelete(false)}>
              Vazgeç
            </Button>
            <Button variant="destructive" onClick={() => remove.mutate()} disabled={remove.isPending}>
              Sil
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

const PAGE_SIZE = 20;

/** Stok hareketleri (en yeni önce); satış hareketleri satışa bağlanır. */
function Movements({ product }: { product: Product }) {
  const list = useInfiniteQuery({
    queryKey: [...stockKey, "movements", product.id],
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET("/api/products/{id}/stock-movements", {
          params: { path: { id: product.id }, query: { page: pageParam, pageSize: PAGE_SIZE } },
        }),
      ),
    initialPageParam: 1,
    getNextPageParam: (last, pages) => (pages.length * PAGE_SIZE < last.total ? pages.length + 1 : undefined),
  });
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <section className="flex flex-col gap-2">
      <h2 className="text-lg font-semibold text-black">Stok hareketleri</h2>
      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        <p className="text-sm text-neutral-600">Henüz hareket yok.</p>
      ) : (
        <>
          <MovementList items={items} />
          {list.hasNextPage && (
            <Button variant="outline" className="self-center" onClick={() => list.fetchNextPage()} disabled={list.isFetchingNextPage}>
              Daha fazla göster
            </Button>
          )}
        </>
      )}
    </section>
  );
}

export function MovementList({ items, showProduct = false }: { items: StockMovement[]; showProduct?: boolean }) {
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {items.map((m) => (
        <li key={m.id} className="flex items-start justify-between gap-3 py-3">
          <div className="flex min-w-0 flex-col gap-0.5">
            <span className="text-sm font-semibold text-black">
              {showProduct ? (
                <Link href={`/panel/urunler/${m.productId}`} className="hover:underline">
                  {m.productName}
                </Link>
              ) : (
                stockMovementTypeNames[m.type]
              )}
            </span>
            <span className="text-xs text-neutral-600">
              {showProduct && `${stockMovementTypeNames[m.type]} · `}
              {formatDayShort(m.date)}
              {m.createdByName && ` · ${m.createdByName}`}
              {m.unitCost != null && ` · birim ${formatMoney(m.unitCost)}`}
            </span>
            {m.saleId && m.saleNo ? (
              <Link href={`/panel/satislar/${m.saleId}`} className="w-fit text-xs font-semibold text-black underline">
                {m.saleNo}
              </Link>
            ) : (
              m.note && <span className="text-xs text-neutral-700">{m.note}</span>
            )}
          </div>
          <div className="flex shrink-0 flex-col items-end">
            <span className="text-sm font-semibold text-black tabular-nums">
              {m.quantity > 0 ? "+" : "−"}
              {formatQuantity(Math.abs(m.quantity), m.unit)}
            </span>
            <span className="text-xs text-neutral-600 tabular-nums" title={formatDateTime(m.createdAt)}>
              Kalan {formatQuantity(m.quantityAfter, m.unit)}
            </span>
          </div>
        </li>
      ))}
    </ul>
  );
}
