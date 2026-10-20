"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useRouter, useSearchParams } from "next/navigation";
import { useCallback, useEffect, type ComponentProps } from "react";

import { meQueryKey, useMeQuery } from "@/components/providers/session-provider";
import { Input } from "@/components/ui/input";
import type { Me } from "@/lib/api/types";
import { safeReturnPath } from "@/lib/validation/rules";

/**
 * Giriş/kayıt sonrası: kurulumu bitmemişse /kurulum, bitmişse geri dönüş adresi veya /panel. Süper admin /platform'a dönerken
 * işletme kurulumu aranmaz (bir işletmeye üye olmayabilir).
 */
export function useAfterAuth() {
  const queryClient = useQueryClient();
  const router = useRouter();
  return useCallback(
    (me: Me, returnPath?: string | null) => {
      queryClient.setQueryData(meQueryKey, me);
      const back = safeReturnPath(returnPath);
      const target =
        back?.startsWith("/platform") && me.user.isPlatformAdmin
          ? back
          : me.business?.setupCompleted
            ? ((back?.startsWith("/panel") ? back : null) ?? "/panel")
            : "/kurulum";
      router.replace(target);
    },
    [queryClient, router],
  );
}

/** Telefon kutusu: başta "+90" görünür; 0532… veya 532… yazılabilir. */
export function PhoneInput(props: ComponentProps<"input">) {
  return (
    <div className="relative">
      <span
        aria-hidden
        className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-base text-muted-foreground"
      >
        +90
      </span>
      <Input
        type="tel"
        inputMode="tel"
        autoComplete="tel-national"
        placeholder="532 123 45 67"
        className="pl-12"
        {...props}
      />
    </div>
  );
}

/** Giriş ve kayıt sayfalarında: zaten oturum açıksa panele (veya kuruluma) geçer. */
export function RedirectIfSignedIn() {
  const { data: me } = useMeQuery();
  const afterAuth = useAfterAuth();
  const params = useSearchParams();

  useEffect(() => {
    if (me) afterAuth(me, params.get("donus"));
  }, [me, afterAuth, params]);

  return null;
}
