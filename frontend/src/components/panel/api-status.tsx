"use client";

import { useQuery } from "@tanstack/react-query";
import { cn } from "cn";

import { api, unwrap } from "@/lib/api/client";

/**
 * API bağlantısını gösterir; apiClient + CORS + OpenAPI tiplerinin uçtan uca çalıştığını doğrular.
 * Sadece geliştirme ortamında görünür; production build'de hiç render edilmez.
 */
export function ApiStatus({ className }: { className?: string }) {
  if (process.env.NODE_ENV !== "development") return null;
  return <ApiStatusIndicator className={className} />;
}

function ApiStatusIndicator({ className }: { className?: string }) {
  const { data, isPending, isError } = useQuery({
    queryKey: ["system", "info"],
    queryFn: () => unwrap(api.GET("/api/system/info")),
  });

  const label = isPending
    ? "API kontrol ediliyor…"
    : isError
      ? "API'ye ulaşılamıyor"
      : `API bağlı · v${data.version}`;

  return (
    <p className={cn("flex items-center gap-2 text-xs", className)} role="status">
      <span
        aria-hidden
        className={cn(
          "size-2 rounded-full",
          isPending ? "bg-neutral-400" : isError ? "bg-danger-600" : "bg-success-600",
        )}
      />
      {label}
    </p>
  );
}
