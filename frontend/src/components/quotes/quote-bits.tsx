"use client";

import { useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";

import { quoteStatusNames, type QuoteStatus } from "@/lib/api/types";

export const quotesKey = ["quotes"] as const;

export function useQuotesRefresh() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: quotesKey }),
      queryClient.invalidateQueries({ queryKey: ["timeline"] }),
    ]);
}

/**
 * Teklif durumu: renk değil biçim ayırır (3 renk kuralı). Onaylandı siyah zemin, satışa dönüştü çerçeveli siyah,
 * reddedildi üstü çizili gri, taslak/gönderildi gri; süresi dolmuşsa "Süresi doldu".
 */
export function QuoteStatusBadge({ status, expired, className }: { status: QuoteStatus; expired?: boolean; className?: string }) {
  const label = expired ? "Süresi doldu" : quoteStatusNames[status];
  return (
    <span
      className={cn(
        "inline-flex h-6 items-center rounded-full border px-2 text-xs font-semibold whitespace-nowrap",
        expired
          ? "border-neutral-400 bg-white text-neutral-700"
          : status === "Accepted"
            ? "border-black bg-black text-white"
            : status === "Converted"
              ? "border-black bg-white text-black"
              : status === "Rejected"
                ? "border-transparent bg-neutral-100 text-neutral-700 line-through"
                : "border-transparent bg-neutral-100 text-black",
        className,
      )}
    >
      {label}
    </span>
  );
}
