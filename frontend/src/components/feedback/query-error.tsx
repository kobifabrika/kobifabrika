"use client";

import { RotateCw } from "lucide-react";
import { cn } from "cn";
import { useState } from "react";

import { Button } from "@/components/ui/button";
import { errorMessage } from "@/lib/api/client";

/**
 * Veri yüklenemediğinde: Türkçe hata + "Tekrar dene". API kapalıysa veya zaman aşımı olduysa ekran
 * "Yükleniyor…"da kalmaz; kullanıcı sayfayı yenilemeden yeniden deneyebilir.
 */
export function QueryError({
  error,
  onRetry,
  message,
  className,
}: {
  error: unknown;
  onRetry: () => unknown;
  /** Hata yerine gösterilecek metin (ör. 404 için "Kayıt bulunamadı."). */
  message?: string;
  className?: string;
}) {
  const [retrying, setRetrying] = useState(false);

  const retry = async () => {
    setRetrying(true);
    try {
      await onRetry();
    } finally {
      setRetrying(false);
    }
  };

  return (
    <div role="alert" className={cn("flex flex-col items-start gap-2", className)}>
      <p className="text-sm text-danger-600">{message ?? errorMessage(error)}</p>
      <Button variant="outline" onClick={retry} disabled={retrying}>
        <RotateCw aria-hidden className={cn("size-4", retrying && "animate-spin")} />
        {retrying ? "Deneniyor…" : "Tekrar dene"}
      </Button>
    </div>
  );
}
