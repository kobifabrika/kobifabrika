"use client";

import { useInfiniteQuery, useMutation } from "@tanstack/react-query";
import { cn } from "cn";
import { Download, FileSpreadsheet, Package, Pencil, Plus, Search, Tags, Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState, type KeyboardEvent } from "react";
import { toast } from "sonner";

import { StockBadge, productsKey, useCatalogRefresh, useCategories, useStockModule } from "@/components/catalog/catalog-bits";
import { EmptyState, selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { API_URL, ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import { productTypeNames, type Category, type Product } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";

const PAGE_SIZE = 25;

type Chip = "all" | "Product" | "Service" | "low" | "inactive";

export function useDebounced<T>(value: T, ms: number) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

/**
 * Ürün/hizmet listesi: ad veya barkodla arama (barkod okutunca karta gider), tür/kritik/pasif çipleri, kategori süzgeci.
 * Masaüstünde tablo, telefonda kart. Alış fiyatı sadece Sahip, Yönetici ve Mali Müşavir'e görünür.
 */
export function ProductList() {
  const { role } = useSession();
  const router = useRouter();
  const params = useSearchParams();
  const stockModule = useStockModule();
  const categories = useCategories();
  const [search, setSearch] = useState("");
  const [chip, setChip] = useState<Chip>("all");
  const [categoryId, setCategoryId] = useState("");
  const [categoriesOpen, setCategoriesOpen] = useState(params.get("kategoriler") === "1");
  const debounced = useDebounced(search.trim(), 300);
  const manage = can(role, "manageCatalog");

  const query = {
    q: debounced || undefined,
    type: chip === "Product" || chip === "Service" ? chip : undefined,
    status: chip === "inactive" ? "inactive" : undefined,
    stock: chip === "low" ? "low" : undefined,
    categoryId: categoryId || undefined,
  };
  const list = useInfiniteQuery({
    queryKey: [...productsKey, "list", query],
    queryFn: ({ pageParam }) =>
      unwrap(api.GET("/api/products", { params: { query: { ...query, page: pageParam, pageSize: PAGE_SIZE } } })),
    initialPageParam: 1,
    getNextPageParam: (last, pages) => (pages.length * PAGE_SIZE < last.total ? pages.length + 1 : undefined),
  });

  const items = list.data?.pages.flatMap((p) => p.items) ?? [];
  const total = list.data?.pages[0]?.total ?? 0;
  const noFilter = !debounced && chip === "all" && !categoryId;

  // Barkod okuyucu arama kutusuna yazıp Enter'a basar: tam eşleşen ürün varsa kartı açılır.
  const onSearchKey = async (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key !== "Enter" || !search.trim()) return;
    e.preventDefault();
    try {
      const product = await unwrap(api.GET("/api/products/by-barcode/{code}", { params: { path: { code: search.trim() } } }));
      router.push(`/panel/urunler/${product.id}`);
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 404)) toast.error(errorMessage(error));
    }
  };

  const chips: { key: Chip; label: string }[] = [
    { key: "all", label: "Tümü" },
    { key: "Product", label: "Ürünler" },
    { key: "Service", label: "Hizmetler" },
    ...(stockModule ? [{ key: "low" as const, label: "Kritik stok" }] : []),
    { key: "inactive", label: "Pasif" },
  ];

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-black">
          Ürünler/Hizmetler{" "}
          {list.data && <span className="text-base font-normal text-neutral-600 tabular-nums">({total})</span>}
        </h1>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => setCategoriesOpen(true)}>
            <Tags aria-hidden />
            Kategoriler
          </Button>
          {manage && (
            <Button asChild variant="outline">
              <Link href="/panel/urunler/ice-aktar">
                <FileSpreadsheet aria-hidden />
                <span className="sr-only sm:not-sr-only">İçe aktar</span>
              </Link>
            </Button>
          )}
          {can(role, "exportData") && (
            <Button asChild variant="outline">
              <a href={`${API_URL}/api/products/export.xlsx`} download>
                <Download aria-hidden />
                <span className="sr-only sm:not-sr-only">Excel&apos;e aktar</span>
              </a>
            </Button>
          )}
          {manage && (
            <Button asChild>
              <Link href="/panel/urunler/yeni">
                <Plus aria-hidden />
                Yeni ürün
              </Link>
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search aria-hidden className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600" />
          <Input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={onSearchKey}
            placeholder="Ad veya barkod ara"
            aria-label="Ürün ara"
            className="pl-9"
          />
        </div>
        {(categories.data?.length ?? 0) > 0 && (
          <select
            aria-label="Kategori"
            className={cn(selectClass, "sm:w-56")}
            value={categoryId}
            onChange={(e) => setCategoryId(e.target.value)}
          >
            <option value="">Tüm kategoriler</option>
            {categories.data!.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
            <option value="00000000-0000-0000-0000-000000000000">Kategorisiz</option>
          </select>
        )}
      </div>

      <div className="-mx-4 overflow-x-auto px-4 lg:mx-0 lg:px-0" role="group" aria-label="Süzgeç">
        <div className="flex min-w-max gap-2">
          {chips.map((c) => (
            <button
              key={c.key}
              type="button"
              onClick={() => setChip(c.key)}
              aria-pressed={chip === c.key}
              className={cn(
                "min-h-11 shrink-0 rounded-full border px-4 text-sm font-medium whitespace-nowrap",
                chip === c.key ? "border-black bg-black text-white" : "border-border bg-background text-black hover:border-black",
              )}
            >
              {c.label}
            </button>
          ))}
        </div>
      </div>

      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        noFilter ? (
          <EmptyState icon={<Package className="size-10" />} title="Henüz ürün veya hizmet yok">
            {manage && (
              <div className="flex flex-wrap gap-2">
                <Button asChild>
                  <Link href="/panel/urunler/yeni">İlk ürünü ekle</Link>
                </Button>
                <Button asChild variant="outline">
                  <Link href="/panel/urunler/ice-aktar">Excel&apos;den aktar</Link>
                </Button>
              </div>
            )}
          </EmptyState>
        ) : (
          <p className="text-sm text-neutral-600">Bu aramaya uyan ürün yok.</p>
        )
      ) : (
        <>
          <ProductTable items={items} showPurchase={can(role, "viewPurchasePrice")} />
          <ProductCards items={items} />
          {list.hasNextPage && (
            <Button variant="outline" className="self-center" onClick={() => list.fetchNextPage()} disabled={list.isFetchingNextPage}>
              {list.isFetchingNextPage ? "Yükleniyor…" : "Daha fazla göster"}
            </Button>
          )}
        </>
      )}

      <CategorySheet open={categoriesOpen} onOpenChange={setCategoriesOpen} canManage={manage} />
    </div>
  );
}

function ProductTable({ items, showPurchase }: { items: Product[]; showPurchase: boolean }) {
  return (
    <div className="hidden overflow-x-auto border-y border-border md:block">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-border text-xs text-neutral-600">
            <th className="py-2 pr-3 font-semibold">Ad</th>
            <th className="py-2 pr-3 font-semibold">Barkod</th>
            <th className="py-2 pr-3 text-right font-semibold">Satış fiyatı</th>
            <th className="py-2 pr-3 text-right font-semibold">KDV</th>
            {showPurchase && <th className="py-2 pr-3 text-right font-semibold">Alış</th>}
            <th className="py-2 font-semibold">Stok</th>
          </tr>
        </thead>
        <tbody>
          {items.map((p) => (
            <tr key={p.id} className="border-b border-border last:border-0 hover:bg-neutral-50">
              <td className="py-2 pr-3">
                <Link href={`/panel/urunler/${p.id}`} className="flex min-h-11 flex-col justify-center font-semibold text-black hover:underline">
                  <span>
                    {p.name}
                    {!p.isActive && <span className="ml-2 text-xs font-normal text-neutral-600">(pasif)</span>}
                  </span>
                  <span className="text-xs font-normal text-neutral-600">
                    {productTypeNames[p.type]}
                    {p.categoryName && ` · ${p.categoryName}`}
                  </span>
                </Link>
              </td>
              <td className="py-2 pr-3 text-neutral-700 tabular-nums">{p.barcode}</td>
              <td className="py-2 pr-3 text-right font-semibold whitespace-nowrap text-black tabular-nums">{formatMoney(p.grossSalePrice)}</td>
              <td className="py-2 pr-3 text-right text-neutral-700 tabular-nums">%{p.vatRate}</td>
              {showPurchase && (
                <td className="py-2 pr-3 text-right whitespace-nowrap text-neutral-700 tabular-nums">
                  {p.purchasePrice != null ? formatMoney(p.purchasePrice) : ""}
                </td>
              )}
              <td className="py-2 whitespace-nowrap">
                <StockBadge product={p} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ProductCards({ items }: { items: Product[] }) {
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border md:hidden">
      {items.map((p) => (
        <li key={p.id}>
          <Link href={`/panel/urunler/${p.id}`} className="flex min-h-11 items-start justify-between gap-3 py-3">
            <span className="flex min-w-0 flex-col gap-1">
              <span className="truncate text-base font-semibold text-black">
                {p.name}
                {!p.isActive && <span className="ml-2 text-xs font-normal text-neutral-600">(pasif)</span>}
              </span>
              <span className="text-sm text-neutral-700">
                {productTypeNames[p.type]}
                {p.categoryName && ` · ${p.categoryName}`}
              </span>
              <StockBadge product={p} />
            </span>
            <span className="shrink-0 text-base font-semibold text-black tabular-nums">{formatMoney(p.grossSalePrice)}</span>
          </Link>
        </li>
      ))}
    </ul>
  );
}

/** Kategoriler: ekle, yeniden adlandır, sil (ürünler kategorisiz kalır), sektöre göre örnekleri ekle. */
function CategorySheet({ open, onOpenChange, canManage }: { open: boolean; onOpenChange: (v: boolean) => void; canManage: boolean }) {
  const categories = useCategories();
  const refresh = useCatalogRefresh();
  const [name, setName] = useState("");
  const [editing, setEditing] = useState<{ id: string; name: string } | null>(null);
  const [deleting, setDeleting] = useState<Category | null>(null);

  const done = async (message?: string) => {
    await refresh();
    if (message) toast.success(message);
  };
  const create = useMutation({
    mutationFn: () => unwrap(api.POST("/api/product-categories", { body: { name: name.trim() } })),
    onSuccess: async () => {
      setName("");
      await done();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const rename = useMutation({
    mutationFn: (c: { id: string; name: string }) =>
      unwrap(api.PUT("/api/product-categories/{id}", { params: { path: { id: c.id } }, body: { name: c.name.trim() } })),
    onSuccess: async () => {
      setEditing(null);
      await done();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE("/api/product-categories/{id}", { params: { path: { id } } })),
    onSuccess: async () => {
      setDeleting(null);
      await done("Kategori silindi.");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const defaults = useMutation({
    mutationFn: () => unwrap(api.POST("/api/product-categories/defaults")),
    onSuccess: () => done("Örnek kategoriler eklendi."),
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full gap-0 overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Kategoriler</SheetTitle>
          <SheetDescription>Silinen kategorinin ürünleri &quot;Kategorisiz&quot; olur.</SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-4 px-4 pb-6">
          {canManage && (
            <form
              className="flex gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (name.trim()) create.mutate();
              }}
            >
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Yeni kategori" aria-label="Yeni kategori adı" maxLength={100} />
              <Button type="submit" disabled={!name.trim() || create.isPending}>
                Ekle
              </Button>
            </form>
          )}
          {categories.isError && <QueryError error={categories.error} onRetry={categories.refetch} />}
          {categories.data?.length === 0 && (
            <div className="flex flex-col items-start gap-2">
              <p className="text-sm text-neutral-600">Henüz kategori yok.</p>
              {canManage && (
                <Button variant="outline" onClick={() => defaults.mutate()} disabled={defaults.isPending}>
                  Sektörüme göre örnekleri ekle
                </Button>
              )}
            </div>
          )}
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {categories.data?.map((c) => (
              <li key={c.id} className="flex min-h-14 items-center gap-2 py-2">
                {editing?.id === c.id ? (
                  <form
                    className="flex flex-1 gap-2"
                    onSubmit={(e) => {
                      e.preventDefault();
                      if (editing.name.trim()) rename.mutate(editing);
                    }}
                  >
                    <Input
                      autoFocus
                      value={editing.name}
                      onChange={(e) => setEditing({ ...editing, name: e.target.value })}
                      aria-label="Kategori adı"
                      maxLength={100}
                    />
                    <Button type="submit" disabled={rename.isPending}>
                      Kaydet
                    </Button>
                  </form>
                ) : (
                  <>
                    <span className="flex-1 text-sm text-black">
                      {c.name} <span className="text-neutral-600 tabular-nums">({c.productCount})</span>
                    </span>
                    {canManage && (
                      <>
                        <Button variant="ghost" size="icon" aria-label={`${c.name}: yeniden adlandır`} onClick={() => setEditing({ id: c.id, name: c.name })}>
                          <Pencil aria-hidden />
                        </Button>
                        <Button variant="ghost" size="icon" aria-label={`${c.name}: sil`} onClick={() => setDeleting(c)}>
                          <Trash2 aria-hidden />
                        </Button>
                      </>
                    )}
                  </>
                )}
              </li>
            ))}
          </ul>
          {deleting && (
            <div className="flex flex-col gap-3 border border-border p-3">
              <p className="text-sm text-black">
                &quot;{deleting.name}&quot; silinsin mi? {deleting.productCount > 0 && `${deleting.productCount} ürün kategorisiz kalır.`}
              </p>
              <div className="grid grid-cols-2 gap-2">
                <Button variant="outline" onClick={() => setDeleting(null)}>
                  Vazgeç
                </Button>
                <Button variant="destructive" onClick={() => remove.mutate(deleting.id)} disabled={remove.isPending}>
                  Sil
                </Button>
              </div>
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
