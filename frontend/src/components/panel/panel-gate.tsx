"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";

import { Wordmark } from "@/components/brand/wordmark";
import { SessionProvider, useMeQuery } from "@/components/providers/session-provider";
import { ApiError } from "@/lib/api/client";

/**
 * Panelin asıl oturum kontrolü (proxy.ts sadece cookie varlığına bakar):
 * oturum yoksa /giris'e (geri dönüş adresiyle), işletme kurulumu bitmemişse /kurulum'a yönlendirir.
 */
export function PanelGate({ children }: { children: ReactNode }) {
  const { data: me, error } = useMeQuery();
  const router = useRouter();
  const pathname = usePathname();

  const unauthenticated = error instanceof ApiError && error.status === 401;
  const needsSetup = !!me && !me.business?.setupCompleted;

  useEffect(() => {
    if (unauthenticated) router.replace(`/giris?donus=${encodeURIComponent(pathname)}`);
    else if (needsSetup) router.replace("/kurulum");
  }, [unauthenticated, needsSetup, pathname, router]);

  if (error && !unauthenticated) {
    return (
      <FullScreenMessage>
        <p className="text-base font-semibold text-black">Sunucuya ulaşılamıyor</p>
        <p className="text-sm text-neutral-600">İnternet bağlantınızı kontrol edip sayfayı yenileyin.</p>
      </FullScreenMessage>
    );
  }

  if (!me || needsSetup) {
    return (
      <FullScreenMessage>
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </FullScreenMessage>
    );
  }

  return <SessionProvider me={me}>{children}</SessionProvider>;
}

function FullScreenMessage({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-3 bg-background px-4 text-center">
      <Wordmark className="text-2xl" />
      {children}
    </div>
  );
}
