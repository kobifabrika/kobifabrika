"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { TriangleAlert } from "lucide-react";

import { useSession } from "@/components/providers/session-provider";
import { api, unwrap } from "@/lib/api/client";
import type { Product } from "@/lib/api/types";
import { formatQuantity } from "@/lib/sales";

export const productsKey = ["products"] as const;
export const categoriesKey = ["product-categories"] as const;
export const stockKey = ["stock"] as const;

export function useVatRates() {
  return useQuery({
    queryKey: ["vat-rates"],
    queryFn: () => unwrap(api.GET("/api/vat-rates")),
    staleTime: Infinity,
  });
}

export function useCategories() {
  return useQuery({ queryKey: categoriesKey, queryFn: () => unwrap(api.GET("/api/product-categories")) });
}

/** İşletmede Stok modülü açık mı (Ayarlar → Modüller). */
export function useStockModule() {
  return useSession().business.modules.stock;
}

/** Ürün/stok değişince ilgili sorgular tazelenir. */
export function useCatalogRefresh() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: productsKey }),
      queryClient.invalidateQueries({ queryKey: categoriesKey }),
      queryClient.invalidateQueries({ queryKey: stockKey }),
    ]);
}

/**
 * Stok gösterimi: normal stok sade, kritik seviye gri kutu + solda mavi çizgi, eksi stok uyarı ikonu ve kalın yazı.
 * Renk değil kelime ("Kritik", "Eksi stok") ayrım yapar (docs/04-tasarim.md).
 */
export function StockBadge({ product, className }: { product: Pick<Product, "trackStock" | "stockQuantity" | "unit" | "isLowStock">; className?: string }) {
  if (!product.trackStock) return null;
  const quantity = formatQuantity(product.stockQuantity, product.unit);
  if (product.stockQuantity < 0)
    return (
      <span className={cn("inline-flex items-center gap-1 text-sm font-semibold text-black tabular-nums", className)}>
        <TriangleAlert aria-hidden className="size-4" />
        Eksi stok {quantity}
      </span>
    );
  if (product.isLowStock)
    return (
      <span
        className={cn(
          "inline-flex items-center border-l-2 border-brand bg-neutral-100 px-2 py-0.5 text-sm text-black tabular-nums",
          className,
        )}
      >
        Kritik · {quantity}
      </span>
    );
  return <span className={cn("text-sm text-neutral-700 tabular-nums", className)}>{quantity}</span>;
}

export type Choice<T extends string> = { value: T; label: string };

/** Birkaç seçenekten biri (tür, KDV, indirim tipi): yan yana düğmeler, seçili olan siyah. Dokunma alanı 44px. */
export function ChoiceGroup<T extends string>({
  label,
  options,
  value,
  onChange,
  className,
}: {
  label: string;
  options: Choice<T>[];
  value: T;
  onChange: (value: T) => void;
  className?: string;
}) {
  return (
    <div role="radiogroup" aria-label={label} className={cn("flex flex-wrap gap-2", className)}>
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          role="radio"
          aria-checked={value === o.value}
          onClick={() => onChange(o.value)}
          className={cn(
            "min-h-11 min-w-14 rounded-lg border px-3 text-sm font-semibold",
            value === o.value ? "border-black bg-black text-white" : "border-border bg-background text-black hover:border-black",
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

/** Etiketli seçim grubu (form alanı gibi): başlık, düğmeler, hata. */
export function ChoiceField<T extends string>({
  label,
  error,
  ...group
}: {
  label: string;
  error?: string;
  options: Choice<T>[];
  value: T;
  onChange: (value: T) => void;
}) {
  return (
    <div className="flex flex-col gap-2">
      <span className="text-sm font-semibold text-foreground">{label}</span>
      <ChoiceGroup label={label} {...group} />
      {error && (
        <p role="alert" className="text-sm text-danger-600">
          {error}
        </p>
      )}
    </div>
  );
}
