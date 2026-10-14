"use client";

import { useQuery } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { forwardRef, useState, type KeyboardEvent } from "react";
import { toast } from "sonner";

import { StockBadge } from "@/components/catalog/catalog-bits";
import { useDebounced } from "@/components/catalog/product-list";
import { Input } from "@/components/ui/input";
import { ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import type { Product, ServicePackage } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";

/** Barkodla tam eşleşen ürün (yoksa null). */
export async function findByBarcode(code: string): Promise<Product | null> {
  try {
    return await unwrap(api.GET("/api/products/by-barcode/{code}", { params: { path: { code } } }));
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

/**
 * Ürün arama kutusu: ad veya barkod. Enter'a basılınca (barkod okuyucu Enter gönderir) önce barkodla tam eşleşme aranır;
 * bulunursa doğrudan seçilir, yoksa tek sonuç varsa o seçilir. Pasif ürünler listelenmez. packages verilirse (hızlı satış)
 * adı eşleşen aktif paketler "Paketler" grubunda çıkar.
 */
export const ProductSearch = forwardRef<
  HTMLInputElement,
  {
    onPick: (product: Product) => void;
    onNotFound?: (code: string) => void;
    trackedOnly?: boolean;
    placeholder?: string;
    autoFocus?: boolean;
    packages?: ServicePackage[];
    onPickPackage?: (pkg: ServicePackage) => void;
  }
>(function ProductSearch(
  { onPick, onNotFound, trackedOnly = false, placeholder = "Ürün adı veya barkod", autoFocus, packages, onPickPackage },
  ref,
) {
  const [text, setText] = useState("");
  const debounced = useDebounced(text.trim(), 200);
  const results = useQuery({
    queryKey: ["products", "search", debounced, trackedOnly],
    queryFn: () =>
      unwrap(
        api.GET("/api/products", {
          params: { query: { q: debounced, pageSize: 8, stock: trackedOnly ? "tracked" : undefined } },
        }),
      ),
    enabled: debounced.length >= 1,
  });

  const pick = (product: Product) => {
    onPick(product);
    setText("");
  };

  const onKeyDown = async (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key !== "Enter") return;
    e.preventDefault();
    const code = text.trim();
    if (!code) return;
    try {
      const exact = await findByBarcode(code);
      if (exact && exact.isActive && (!trackedOnly || exact.trackStock)) return pick(exact);
      const items = results.data?.items ?? [];
      if (items.length === 1 && debounced === code) return pick(items[0]);
      if (!exact && /^\d{4,}$/.test(code)) {
        onNotFound?.(code);
        setText("");
      }
    } catch (error) {
      toast.error(errorMessage(error));
    }
  };

  const items = debounced ? (results.data?.items ?? []) : [];
  const needle = debounced.toLocaleLowerCase("tr-TR");
  const packageMatches =
    needle && packages && onPickPackage
      ? packages.filter((p) => p.isActive && (p.name.toLocaleLowerCase("tr-TR").includes(needle) || p.serviceName.toLocaleLowerCase("tr-TR").includes(needle))).slice(0, 5)
      : [];
  return (
    <div className="flex flex-col gap-2">
      <div className="relative">
        <Search aria-hidden className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600" />
        <Input
          ref={ref}
          type="search"
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={onKeyDown}
          placeholder={placeholder}
          aria-label="Ürün ara"
          autoComplete="off"
          autoFocus={autoFocus}
          className="pl-9"
        />
      </div>
      {items.length > 0 && (
        <ul className="flex flex-col divide-y divide-border border border-border" aria-label="Arama sonuçları">
          {items.map((p) => (
            <li key={p.id}>
              <button
                type="button"
                onClick={() => pick(p)}
                className="flex min-h-12 w-full items-center justify-between gap-3 px-3 py-2 text-left hover:bg-neutral-50"
              >
                <span className="flex min-w-0 flex-col">
                  <span className="truncate text-sm font-semibold text-black">{p.name}</span>
                  {p.trackStock && <StockBadge product={p} className="text-xs" />}
                </span>
                <span className="shrink-0 text-sm font-semibold text-black tabular-nums">{formatMoney(p.grossSalePrice)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {packageMatches.length > 0 && (
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-neutral-700">Paketler</span>
          <ul className="flex flex-col divide-y divide-border border border-border" aria-label="Paketler">
            {packageMatches.map((p) => (
              <li key={p.id}>
                <button
                  type="button"
                  onClick={() => {
                    onPickPackage!(p);
                    setText("");
                  }}
                  className="flex min-h-12 w-full items-center justify-between gap-3 px-3 py-2 text-left hover:bg-neutral-50"
                >
                  <span className="flex min-w-0 flex-col">
                    <span className="truncate text-sm font-semibold text-black">{p.name}</span>
                    <span className="text-xs text-neutral-600">
                      Paket · {p.sessionCount} seans · {p.validityDays} gün
                    </span>
                  </span>
                  <span className="shrink-0 text-sm font-semibold text-black tabular-nums">{formatMoney(p.price)}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
      {debounced && results.isSuccess && items.length === 0 && packageMatches.length === 0 && (
        <p className="text-sm text-neutral-600">Bu aramaya uyan ürün yok.</p>
      )}
    </div>
  );
});
